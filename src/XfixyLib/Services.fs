namespace Xfixy.Services

open Xfixy
open System
open System.Diagnostics
open System.IO
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Configuration
open System.IO.Pipes
open System.Runtime.ExceptionServices

[<AutoOpen>]
module internal Control =
    open Control

    type ExecContext =
        { Logger: ILogger
          Configuration: IConfiguration }

    type internal FetchScript =
        | Fetch
        | Not

    // https://stackoverflow.com/questions/7168801/how-to-use-reraise-in-async-workflows-in-f
    // https://github.com/fsharp/fslang-suggestions/issues/660
    let inline reraisePreserveStackTrace (ex: Exception) =
        (ExceptionDispatchInfo.Capture ex).Throw()
        Unchecked.defaultof<_>

    let executeAsync
        (ctx: ExecContext)
        (shareResult: string -> CancellationToken -> Task<unit>)
        (ct: CancellationToken)
        =
        task {
            let scriptReloadInterval () =
                let v = ctx.Configuration.GetValue<int>("Worker:Script:ReloadIntervalSeconds", 15)
                v

            let delay () =
                let v = ctx.Configuration.GetValue<int>("Worker:Delay", 4000)
                v

            let stopWatch = Stopwatch()
            let mutable cached = Unchecked.defaultof<_>

            while not ct.IsCancellationRequested do
                ctx.Logger.LogInformation("Worker running at: {time}.", DateTimeOffset.Now)
                let scriptsPath = ctx.Configuration["Worker:Scripts-Path"]

                if not (Directory.Exists scriptsPath) then
                    ctx.Logger.LogWarning("ScriptsPath not found: {scriptsPath}.", scriptsPath)
                else
                    let fetchOrNot =
                        if not stopWatch.IsRunning then
                            stopWatch.Start()
                            Fetch
                        elif stopWatch.Elapsed.TotalSeconds
                             >= scriptReloadInterval () then
                            Fetch
                        else
                            Not

                    let! scriptContentDict =
                        task {
                            match fetchOrNot with
                            | Fetch ->
                                stopWatch.Restart()
                                let scriptFiles = Directory.GetFiles(scriptsPath, "*.ps1")
                                let! res = PSscript.loadScriptsAsync scriptFiles ct
                                cached <- res
                                return res
                            | Not -> return cached
                        }

                    let! resDict = runScriptAsync scriptContentDict ct

                    for kv in resDict do
                        let res = resDict[kv.Key]

                        match res with
                        | Result.Ok psResultAsString ->
                            ctx.Logger.LogInformation("""PowerShell Result: "{psResultAsString}".""", psResultAsString)

                            try
                                do! shareResult psResultAsString ct
                            with
                            | _ ->
                                ctx.Logger.LogError(
                                    """Error calling shareResult with value: "{psResultAsString}".""",
                                    psResultAsString
                                )
                        | Result.Error err -> ctx.Logger.LogError(err)

                do! Task.Delay(delay (), ct)
        }

// https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service
// New-Service -Name Xfinixy -BinaryPathName C:\Users\...\Xfixy.exe
type Worker(logger: ILogger<Worker>, configuration: IConfiguration) =
    inherit BackgroundService()

    let messager = Messager()

    member self.OnMessage
        with public get (): IObservable<string> = messager

    member _.Delay =
        let delay = configuration.GetValue<int>("Worker:Delay", 4000)
        delay

    member _.ScriptReloadInterval =
        let delay = configuration.GetValue<int>("Worker:Script:ReloadIntervalSeconds", 15)
        delay

    override self.ExecuteAsync(ct: CancellationToken) =
        task {
            let mutable _pipeClient =
                new NamedPipeClientStream(".", "Xfixy-pipe", PipeDirection.InOut)

            let ctx =
                { Logger = logger
                  Configuration = configuration }

            let shareResult psResultAsString ct =
                task {
                    let pipeClient = _pipeClient
                    messager.Push psResultAsString

                    if not pipeClient.IsConnected then
                        try
                            do! pipeClient.ConnectAsync(100, ct)
                        with
                        | :? TimeoutException as ex ->
                            ctx.Logger.LogTrace(ex, "[CLIENT] Timeout error.")
                            ()

                    if pipeClient.IsConnected then
                        try
                            let sw = new StreamWriter(pipeClient)
                            sw.AutoFlush <- true
                            // Send a 'message' and wait for client to receive it.
                            do! sw.WriteLineAsync(psResultAsString.AsMemory(), ct)
                            pipeClient.WaitForPipeDrain()
                        with
                        | :? IOException as ex ->
                            // Catch the IOException that is raised if the pipe is broken or disconnected.
                            ctx.Logger.LogWarning(
                                ex,
                                """[CLIENT] IOError calling sendAsync. with message "{message}".""",
                                psResultAsString
                            )
                            // If broken create a new pipe.
                            pipeClient.Close()
                            pipeClient.Dispose()
                            _pipeClient <- new NamedPipeClientStream(".", "Xfixy-pipe", PipeDirection.InOut)
                        | _ as ex ->
                            ctx.Logger.LogError(
                                ex,
                                """Error calling sendAsync. with message "{message}".""",
                                psResultAsString
                            )

                            reraisePreserveStackTrace ex
                }

            return! executeAsync ctx shareResult ct
        }
