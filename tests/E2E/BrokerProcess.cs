using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Netsoft.MessageQueues.E2E.Tests;

/// <summary>
/// ブローカーを<b>本物の別プロセスとして</b>起こす。
/// </summary>
/// <remarks>
/// <para>
/// <c>WebApplicationFactory</c>（メモリ内の輸送）で済ませない。この層で確かめたいのは
/// 「プロセスの外から繋いで配送できるか」なので、境界を跨がない形にすると
/// 検証したいものが消える。tests/Broker は口そのものを、ここは境界を確かめる。
/// </para>
/// <para>
/// 起こすのは <c>dotnet run</c> ではなくビルド済みの dll。<c>dotnet run</c> は MSBuild を
/// 走らせるので、テストの実行中に obj/ を触りに行く可能性がある。
/// </para>
/// </remarks>
public sealed class BrokerProcess : IAsyncDisposable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);

    private readonly Process _process;
    private readonly StringBuilder _output;

    private BrokerProcess(Process process, StringBuilder output, Uri baseAddress)
    {
        _process = process;
        _output = output;
        BaseAddress = baseAddress;
    }

    /// <summary>立ち上がったブローカーの場所。</summary>
    public Uri BaseAddress { get; }

    /// <remarks>
    /// DB ファイルは受け取るだけで、持たない ── 落として立て直す試験では
    /// 1 台目と 2 台目が同じファイルを使うので、プロセスの後始末が消してはいけない
    /// （<see cref="TemporaryDatabase"/> の注記）。
    /// </remarks>
    public static async Task<BrokerProcess> StartAsync(
        string databasePath,
        params (string Topic, string Name, int Lanes)[] subscriptions)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);

        int port = FreePort();
        Uri baseAddress = new($"http://127.0.0.1:{port}");

        ProcessStartInfo start = new("dotnet", BrokerAssemblyPath())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.Environment["ASPNETCORE_URLS"] = baseAddress.ToString();
        start.Environment["MessageQueue__DatabasePath"] = databasePath;
        start.Environment["MessageQueue__RetryDelaySeconds"] = "0.05";

        for (int i = 0; i < subscriptions.Length; i++)
        {
            (string topic, string name, int lanes) = subscriptions[i];
            start.Environment[$"MessageQueue__Subscriptions__{i}__Topic"] = topic;
            start.Environment[$"MessageQueue__Subscriptions__{i}__Name"] = name;
            start.Environment[$"MessageQueue__Subscriptions__{i}__Lanes"] = lanes.ToString();
        }

        Process process = new() { StartInfo = start };
        StringBuilder output = new();

        // 読まないと、出力の受け口が詰まったところで子が止まる。失敗したときの
        // 手がかりでもあるので、溜めておいて例外に載せる。
        process.OutputDataReceived += (_, e) => output.AppendLine(e.Data);
        process.ErrorDataReceived += (_, e) => output.AppendLine(e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        BrokerProcess broker = new(process, output, baseAddress);

        await broker.WaitUntilReadyAsync();
        return broker;
    }

    /// <summary>ブローカーを落とす。DB はそのまま残るので、同じ場所で立て直せる。</summary>
    public async Task KillAsync()
    {
        if (_process.HasExited)
        {
            return;
        }

        _process.Kill(entireProcessTree: true);
        await _process.WaitForExitAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await KillAsync();
        _process.Dispose();
    }

    private async Task WaitUntilReadyAsync()
    {
        using HttpClient probe = new() { BaseAddress = BaseAddress, Timeout = TimeSpan.FromSeconds(2) };
        using CancellationTokenSource timeout = new(StartTimeout);

        while (true)
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException($"ブローカーが起動せずに終了しました。\n{_output}");
            }

            try
            {
                // 応答の中身は問わない。何であれ返るなら口は開いている。
                using HttpResponseMessage _ = await probe.GetAsync("/", timeout.Token);
                return;
            }
            catch (HttpRequestException)
            {
                // まだ聴いていない。
            }
            catch (TaskCanceledException) when (!timeout.IsCancellationRequested)
            {
                // 1 回ぶんの待ちが尽きただけ。
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException($"ブローカーが時間内に立ち上がりませんでした。\n{_output}");
            }
        }
    }

    /// <remarks>
    /// 空きポートを OS に選ばせてから手放す。掴んだまま渡す手が無いので、
    /// 選んでから使うまでの隙間は残る ── 単一のテスト機で当たる確率は無視できる。
    /// </remarks>
    private static int FreePort()
    {
        TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    /// <remarks>
    /// 自分の出力先（tests/E2E/bin/&lt;構成&gt;/&lt;TFM&gt;）から、同じ構成・同じ TFM の
    /// ブローカーを探す。構成を決め打ちすると Release のビルドで見つからなくなる。
    /// </remarks>
    private static string BrokerAssemblyPath()
    {
        DirectoryInfo output = new(AppContext.BaseDirectory);
        string framework = output.Name;
        string configuration = output.Parent!.Name;

        DirectoryInfo root = output;
        while (!File.Exists(Path.Combine(root.FullName, "Netsoft.MessageQueues.slnx")))
        {
            root = root.Parent
                ?? throw new InvalidOperationException("リポジトリの根が見つかりません。");
        }

        string path = Path.Combine(
            root.FullName, "src", "Broker", "bin", configuration, framework, "Netsoft.MessageQueues.Broker.dll");

        return File.Exists(path)
            ? path
            : throw new InvalidOperationException($"ブローカーがビルドされていません: {path}");
    }
}
