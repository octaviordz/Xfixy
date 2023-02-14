namespace Xfixy.Server

open System
open System.IO
open System.Threading
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Configuration
open System.IO.Pipes
open Xfixy.Control

type Worker(logger: ILogger<Worker>, configuration: IConfiguration) =
    inherit BackgroundService()

    let pipeServer: NamedPipeServerStream =
        new NamedPipeServerStream("Xfixy-pipe", PipeDirection.InOut)

    let messager = Messager()

    member self.OnMessage
        with public get (): IObservable<string> = messager

    override self.ExecuteAsync(ct: CancellationToken) =
        task {
            while not ct.IsCancellationRequested do
                try
                    logger.LogInformation("[SERVER] Worker running at: {time}.", DateTimeOffset.Now)
                    do! pipeServer.WaitForConnectionAsync(cancellationToken = ct)
                    use sr = new StreamReader(pipeServer)
                    logger.LogTrace("[SERVER] Current TransmissionMode: {0}.", pipeServer.TransmissionMode)
                    //use sw = new StreamWriter(pipeServer)
                    //do! sw.WriteLineAsync("Get-ProcessID".AsMemory(), ct)
                    let rec readAndHandleLine () =
                        task {
                            let! line = sr.ReadLineAsync ct

                            if not (isNull line) then
                                messager.Push line

                            logger.LogTrace("[SERVER] Echo: " + line)

                            if ct.IsCancellationRequested || isNull line then
                                return ()
                            else
                                return! readAndHandleLine ()
                        }

                    do! readAndHandleLine ()
                with
                | _ as ex -> logger.LogError(ex, "[SERVER] Error.")
        }
