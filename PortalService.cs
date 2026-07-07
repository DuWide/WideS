using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace DevCockpit;

public sealed class PortalNoteRequest
{
    public string Title { get; init; } = "";
    public string Text { get; init; } = "";
    public string Author { get; init; } = "Гость";
}

public sealed class PortalTaskRequest
{
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Author { get; init; } = "Гость";
}

public sealed class PortalState
{
    public string UserName { get; init; } = "";
    public string PortalUrl { get; init; } = "";
    public string? ActiveProject { get; init; }
    public string? ActiveSince { get; init; }
    public bool HasActiveProject { get; init; }
    public List<PortalTaskRow> TasksInProgress { get; init; } = [];
    public List<PortalTaskRow> TasksProject { get; init; } = [];
    public List<PortalTaskRow> TasksCommon { get; init; } = [];
    public List<PortalConnectionRow> Connections { get; init; } = [];
    public List<PortalNoteRow> Notes { get; init; } = [];
}

public sealed class PortalConnectionRow
{
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public string Address { get; init; } = "";
}

public sealed class PortalTaskRow
{
    public string Title { get; init; } = "";
    public string Status { get; init; } = "";
    public string Project { get; init; } = "";
}

public sealed class PortalNoteRow
{
    public string Title { get; init; } = "";
    public string Preview { get; init; } = "";
    public string UpdatedAt { get; init; } = "";
}

public sealed class PortalService : IDisposable
{
    private readonly ConcurrentBag<HttpListener> _listeners = [];
    private readonly List<HttpListenerContext> _sseClients = [];
    private readonly object _sseLock = new();
    private CancellationTokenSource? _cts;
    private Func<PortalState>? _getState;
    private Action<PortalNoteRequest>? _onNote;
    private Action<PortalTaskRequest>? _onTask;
    private int _port;

    public bool IsRunning { get; private set; }
    public string? LastError { get; private set; }
    public IReadOnlyList<string> BoundUrls { get; private set; } = [];

    public void Start(
        int port,
        Func<PortalState> getState,
        Action<PortalNoteRequest> onNote,
        Action<PortalTaskRequest> onTask)
    {
        Stop();
        _port = port;
        _getState = getState;
        _onNote = onNote;
        _onTask = onTask;
        _cts = new CancellationTokenSource();

        var prefixes = BuildPrefixes(port);
        var activeUrls = new List<string>();
        var errors = new List<string>();
        var startedAny = false;

        foreach (var prefix in prefixes)
        {
            try
            {
                var listener = new HttpListener();
                listener.Prefixes.Add(prefix);
                listener.Start();
                _listeners.Add(listener);
                activeUrls.Add(prefix);
                startedAny = true;
                _ = Task.Run(() => AcceptLoop(listener, _cts.Token));
            }
            catch (Exception ex)
            {
                errors.Add($"{prefix}: {ex.Message}");
            }
        }

        BoundUrls = activeUrls;
        IsRunning = startedAny;
        LastError = startedAny
            ? (errors.Count > 0 ? string.Join("; ", errors) : null)
            : (errors.Count > 0 ? string.Join("; ", errors) : "Не удалось запустить HttpListener.");
    }

    public void Stop()
    {
        _cts?.Cancel();
        foreach (var listener in _listeners)
        {
            try
            {
                listener.Stop();
                listener.Close();
            }
            catch
            {
                // ignore
            }
        }

        _listeners.Clear();
        lock (_sseLock)
        {
            _sseClients.Clear();
        }

        IsRunning = false;
    }

    public void NotifyUpdate()
    {
        if (!IsRunning || _getState is null)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(_getState());
        BroadcastSse(payload);
    }

    public void Dispose() => Stop();

    private async Task AcceptLoop(HttpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var context = await listener.GetContextAsync().WaitAsync(token);
                _ = Task.Run(() => HandleRequest(context), token);
            }
            catch when (token.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                await Task.Delay(250, token);
            }
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath?.TrimEnd('/') ?? "";
            if (context.Request.HttpMethod == "GET" && (path is "" or "/"))
            {
                WriteHtml(context.Response, PortalPageHtml());
                return;
            }

            if (context.Request.HttpMethod == "GET" && path.Equals("/api/state", StringComparison.OrdinalIgnoreCase))
            {
                WriteJson(context.Response, _getState?.Invoke() ?? new PortalState());
                return;
            }

            if (context.Request.HttpMethod == "GET" && path.Equals("/api/events", StringComparison.OrdinalIgnoreCase))
            {
                HandleSse(context);
                return;
            }

            if (context.Request.HttpMethod == "POST" && path.Equals("/api/notes", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                var body = reader.ReadToEnd();
                var request = JsonSerializer.Deserialize<PortalNoteRequest>(body, JsonOptions()) ?? new PortalNoteRequest();
                _onNote?.Invoke(request);
                WriteJson(context.Response, new { ok = true });
                return;
            }

            if (context.Request.HttpMethod == "POST" && path.Equals("/api/tasks", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                var body = reader.ReadToEnd();
                var request = JsonSerializer.Deserialize<PortalTaskRequest>(body, JsonOptions()) ?? new PortalTaskRequest();
                _onTask?.Invoke(request);
                WriteJson(context.Response, new { ok = true });
                return;
            }

            context.Response.StatusCode = 404;
            context.Response.Close();
        }
        catch
        {
            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch
            {
                // ignore
            }
        }
    }

    private void HandleSse(HttpListenerContext context)
    {
        var response = context.Response;
        response.ContentType = "text/event-stream";
        response.Headers.Add("Cache-Control", "no-cache");
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.SendChunked = true;

        lock (_sseLock)
        {
            _sseClients.Add(context);
        }

        try
        {
            var payload = JsonSerializer.Serialize(_getState?.Invoke() ?? new PortalState());
            WriteSseEvent(response.OutputStream, payload);
            response.OutputStream.Flush();

            while (response.OutputStream.CanWrite && _cts is { IsCancellationRequested: false })
            {
                Thread.Sleep(2000);
            }
        }
        catch
        {
            // client disconnected
        }
        finally
        {
            lock (_sseLock)
            {
                _sseClients.Remove(context);
            }

            try
            {
                response.OutputStream.Close();
                response.Close();
            }
            catch
            {
                // ignore
            }
        }
    }

    private void BroadcastSse(string payload)
    {
        List<HttpListenerContext> clients;
        lock (_sseLock)
        {
            clients = _sseClients.ToList();
        }

        foreach (var client in clients)
        {
            try
            {
                WriteSseEvent(client.Response.OutputStream, payload);
                client.Response.OutputStream.Flush();
            }
            catch
            {
                lock (_sseLock)
                {
                    _sseClients.Remove(client);
                }
            }
        }
    }

    private static void WriteSseEvent(Stream stream, string payload)
    {
        var bytes = Encoding.UTF8.GetBytes($"data: {payload}\n\n");
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteJson(HttpListenerResponse response, object data)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions());
        var buffer = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json; charset=utf-8";
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.Close();
    }

    private static void WriteHtml(HttpListenerResponse response, string html)
    {
        var buffer = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.Close();
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static List<string> BuildPrefixes(int port)
    {
        var prefixes = new List<string>
        {
            $"http://127.0.0.1:{port}/",
            $"http://localhost:{port}/"
        };

        foreach (var address in GetLocalIPv4())
        {
            prefixes.Add($"http://{address}:{port}/");
        }

        return prefixes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> GetLocalIPv4()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            foreach (var address in nic.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(address.Address))
                {
                    yield return address.Address.ToString();
                }
            }
        }
    }

    private static string PortalPageHtml() => """
<!DOCTYPE html>
<html lang="ru">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>WideS Portal</title>
  <style>
    :root { color-scheme: dark; --bg:#0b1016; --panel:#151b22; --line:#2c566b; --text:#e8edf2; --muted:#8fa0b2; --accent:#20b2aa; }
    * { box-sizing:border-box; }
    body { margin:0; font-family:Segoe UI,system-ui,sans-serif; background:radial-gradient(circle at top,#13202b,var(--bg)); color:var(--text); }
    .wrap { max-width:1200px; margin:0 auto; padding:24px; }
    .hero { margin-bottom:24px; }
    .badge { display:inline-flex; align-items:center; gap:8px; background:rgba(32,178,170,.12); border:1px solid rgba(32,178,170,.35); color:var(--accent); padding:8px 12px; border-radius:999px; font-size:12px; font-weight:600; }
    h1 { margin:12px 0 6px; font-size:32px; }
    .sub { color:var(--muted); }
    .grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(280px,1fr)); gap:16px; }
    .card { background:rgba(21,27,34,.92); border:1px solid var(--line); border-radius:18px; padding:18px; box-shadow:0 20px 60px rgba(0,0,0,.25); }
    .card h2 { margin:0 0 12px; font-size:16px; color:var(--accent); }
    .row { display:flex; justify-content:space-between; gap:12px; padding:10px 0; border-bottom:1px solid rgba(255,255,255,.06); }
    .row:last-child { border-bottom:none; }
    .muted { color:var(--muted); font-size:13px; }
    .pill { background:#1d2731; border-radius:999px; padding:4px 10px; font-size:12px; white-space:nowrap; }
    form { display:grid; gap:10px; margin-top:8px; }
    input, textarea, button { font:inherit; }
    input, textarea { width:100%; background:#0f141a; color:var(--text); border:1px solid #304556; border-radius:12px; padding:12px; }
    button { background:var(--accent); color:#041014; border:none; border-radius:12px; padding:12px 16px; font-weight:700; cursor:pointer; }
    .live { width:8px; height:8px; border-radius:50%; background:#35d07f; box-shadow:0 0 12px #35d07f; }
    .warn { color:#ffb347; font-size:13px; margin-top:8px; }
  </style>
</head>
<body>
  <div class="wrap">
    <div class="hero">
      <div class="badge"><span class="live"></span> Live portal</div>
      <h1 id="userName">WideS</h1>
      <div class="sub" id="activeContext">Ожидание данных...</div>
      <div class="warn" id="projectWarn" style="display:none">Выберите проект в WideS — тогда появятся заметки, задачи и подключения проекта.</div>
    </div>
    <div class="grid">
      <div class="card">
        <h2>В работе</h2>
        <div id="tasksNow"></div>
      </div>
      <div class="card">
        <h2>Задачи проекта</h2>
        <div id="tasksProject"></div>
      </div>
      <div class="card">
        <h2>Общие задачи</h2>
        <div id="tasksCommon"></div>
      </div>
      <div class="card">
        <h2>Подключения проекта</h2>
        <div id="connections"></div>
      </div>
      <div class="card">
        <h2>Заметки проекта</h2>
        <div id="notes"></div>
      </div>
      <div class="card">
        <h2>Оставить заметку</h2>
        <form id="noteForm">
          <input id="noteAuthor" placeholder="Ваше имя" />
          <input id="noteTitle" placeholder="Заголовок" required />
          <textarea id="noteText" rows="4" placeholder="Текст заметки" required></textarea>
          <button type="submit">Отправить заметку</button>
        </form>
      </div>
      <div class="card">
        <h2>Новая задача</h2>
        <form id="taskForm">
          <input id="taskAuthor" placeholder="Ваше имя" />
          <input id="taskTitle" placeholder="Название задачи" required />
          <textarea id="taskText" rows="4" placeholder="Описание"></textarea>
          <button type="submit">Отправить задачу</button>
        </form>
      </div>
    </div>
  </div>
  <script>
    const el = id => document.getElementById(id);
    function row(left, right) {
      return `<div class="row"><div>${left}</div><div class="muted">${right ?? ''}</div></div>`;
    }
    function renderTasks(list, emptyText, showStatus) {
      if (!list || !list.length) return row(emptyText, '');
      return list.map(t => row(
        `<strong>${t.title}</strong>${t.project ? `<div class="muted">${t.project}</div>` : ''}`,
        showStatus ? `<span class="pill">${t.status}</span>` : ''
      )).join('');
    }
    function render(data) {
      el('userName').textContent = data.userName || 'WideS';
      const hasProject = !!data.hasActiveProject;
      el('projectWarn').style.display = hasProject ? 'none' : 'block';
      const ctx = hasProject
        ? `Проект: ${data.activeProject || '—'}${data.activeSince ? ' · с ' + data.activeSince : ''}`
        : 'Проект не выбран в WideS';
      el('activeContext').textContent = ctx;
      el('tasksNow').innerHTML = renderTasks(data.tasksInProgress, 'Нет задач в работе', true);
      el('tasksProject').innerHTML = hasProject
        ? renderTasks(data.tasksProject, 'Задач проекта нет', true)
        : row('Выберите проект в WideS', '');
      el('tasksCommon').innerHTML = renderTasks(data.tasksCommon, 'Общих задач нет', true);
      el('connections').innerHTML = hasProject
        ? ((data.connections || []).length
            ? data.connections.map(c => row(`<strong>${c.name}</strong><div class="muted">${c.type}</div>`, c.address)).join('')
            : row('Подключений нет', ''))
        : row('Выберите проект в WideS', '');
      el('notes').innerHTML = hasProject
        ? ((data.notes || []).length
            ? data.notes.map(n => row(`<strong>${n.title}</strong><div class="muted">${n.preview}</div>`, n.updatedAt)).join('')
            : row('Заметок проекта нет', ''))
        : row('Выберите проект в WideS', '');
    }
    async function refresh() {
      const res = await fetch('/api/state');
      render(await res.json());
    }
    el('noteForm').addEventListener('submit', async e => {
      e.preventDefault();
      await fetch('/api/notes', {
        method:'POST',
        headers:{'Content-Type':'application/json'},
        body: JSON.stringify({
          author: el('noteAuthor').value,
          title: el('noteTitle').value,
          text: el('noteText').value
        })
      });
      el('noteTitle').value = '';
      el('noteText').value = '';
      await refresh();
    });
    el('taskForm').addEventListener('submit', async e => {
      e.preventDefault();
      await fetch('/api/tasks', {
        method:'POST',
        headers:{'Content-Type':'application/json'},
        body: JSON.stringify({
          author: el('taskAuthor').value,
          title: el('taskTitle').value,
          description: el('taskText').value
        })
      });
      el('taskTitle').value = '';
      el('taskText').value = '';
      await refresh();
    });
    refresh();
    const source = new EventSource('/api/events');
    source.onmessage = e => render(JSON.parse(e.data));
  </script>
</body>
</html>
""";
}
