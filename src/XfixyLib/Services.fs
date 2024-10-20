namespace Xfixy.Services

open System.IO
open System.Threading
open Microsoft.Extensions.Logging
open System.Collections
open Elmish
open Xfixy.Control.PSscript

[<AutoOpen>]
module internal Internal =
    type InternalLogLevel = Xfixy.Logging.LogLevel

    let setLogger (logger: ILogger) =
        Xfixy.Logging.setLogFunc (fun logLevel msgFunc ex msgParameters ->
            match logLevel with
            | InternalLogLevel.Trace -> logger.Log(LogLevel.Trace, ex, msgFunc.Invoke(), msgParameters)
            | InternalLogLevel.Debug -> logger.Log(LogLevel.Debug, ex, msgFunc.Invoke(), msgParameters)
            | InternalLogLevel.Info -> logger.Log(LogLevel.Information, ex, msgFunc.Invoke(), msgParameters)
            | InternalLogLevel.Warn -> logger.Log(LogLevel.Warning, ex, msgFunc.Invoke(), msgParameters)
            | InternalLogLevel.Error -> logger.Log(LogLevel.Error, ex, msgFunc.Invoke(), msgParameters)
            | InternalLogLevel.Fatal -> logger.Log(LogLevel.Critical, ex, msgFunc.Invoke(), msgParameters)
            | _ -> ()

            ())

module internal Control =
    open Xfixy.Control

    type ScriptsConfig = { Location: string }

    [<RequireQualifiedAccess>]
    type StatusMessage =
        | None
        | Ok of ResultValue: string
        | Error of ErrorValue: string

    type FetchScriptsResult = Option<Result<unit, string>>

    module FetchScriptsResult =
        let Ok: FetchScriptsResult = Some(Result<unit, string>.Ok())
        let Error error : FetchScriptsResult = Some(Result<unit, string>.Error error)

    type ScriptContentDict = Generic.IDictionary<string, string>
    type ResultDict = Generic.IDictionary<string, RunScriptResultEither>

    type WorkerModel =
        { LastFetchScriptsResult: FetchScriptsResult
          ScriptDict: ScriptContentDict
          ScriptResultDict: Deferred<ResultDict>
          ScriptsConfig: ScriptsConfig
          CancellationToken: CancellationToken }

    type Model = WorkerModel * Client.Model

    type WorkerMsg =
        | FetchScripts of AsyncOperationStatus<Result<ScriptContentDict, exn>>
        | ExecuteScripts of AsyncOperationStatus<Result<ResultDict, exn>>
    //| ConsumeScriptsCompleted

    type Msg =
        | WorkerMsg of WorkerMsg
        | ClientMsg of Client.Msg

    type internal FetchScript =
        | Fetch
        | Not

    [<RequireQualifiedAccess>]
    type internal SubscriptionKind =
        | FetchScripts
        | ExecuteScripts

    let fetchScriptsAsync location ct =
        task {
            let scriptFiles = Directory.GetFiles(location, "*.ps1")
            let! res = PSscript.loadScriptsAsync scriptFiles ct
            return FetchScripts(Finished(Ok res))
        }

    let runScriptsAync scriptContentDict ct =
        task {
            let! resDict = runScriptAsync scriptContentDict ct
            return ExecuteScripts(Finished(Ok resDict))
        }

    let updateWith toMsg model (first, subCmd) =
        let _, b = model
        let model = (first, b)
        (model, Cmd.map toMsg subCmd)

    let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
        let workerModel, clientModel = model

        match msg with
        | ClientMsg clientMsg ->
            let clientModel', clientCmd' = Client.update clientMsg clientModel
            let model' = (workerModel, clientModel')
            (model', Cmd.map ClientMsg clientCmd')
        | WorkerMsg msg ->
            match msg with
            | FetchScripts Started ->
                let location = workerModel.ScriptsConfig.Location
                let ct = workerModel.CancellationToken

                if not (Directory.Exists location) then
                    let ex = DirectoryNotFoundException(location)
                    let cmd = Cmd.ofMsg (FetchScripts(Finished(Error ex)))

                    (workerModel, cmd) |> updateWith WorkerMsg model
                else
                    let fetch location = fetchScriptsAsync location ct

                    let cmd =
                        Cmd.OfTask.either fetch location id (fun ex -> FetchScripts(Finished(Error ex)))

                    (workerModel, cmd) |> updateWith WorkerMsg model
            | FetchScripts(Finished(Error ex)) ->
                let model' =
                    { workerModel with
                        LastFetchScriptsResult = FetchScriptsResult.Error $"ScriptsPath not found: {ex.Message}" }

                (model', Cmd.none) |> updateWith WorkerMsg model
            | FetchScripts(Finished(Ok contentDict)) ->
                let model' =
                    { workerModel with
                        ScriptDict = contentDict
                        LastFetchScriptsResult = FetchScriptsResult.Ok }

                (model', Cmd.none) |> updateWith WorkerMsg model
            | ExecuteScripts Started ->
                let scriptContentDict = workerModel.ScriptDict
                let ct = workerModel.CancellationToken
                let execute scriptContentDict = runScriptsAync scriptContentDict ct

                let model' =
                    { workerModel with
                        ScriptResultDict = InProgress }

                let cmd =
                    Cmd.OfTask.either execute scriptContentDict id (fun ex -> ExecuteScripts(Finished(Error ex)))

                (model', cmd) |> updateWith WorkerMsg model
            | ExecuteScripts(Finished(Error ex)) ->
                let errorMessage = Client.Note.Error $"Error executing script. {ex.Message}"

                let clientModel' =
                    { clientModel with
                        Note = [ errorMessage ] }

                let completeModel = (workerModel, clientModel')
                let msg = ClientMsg Client.Msg.Send
                (completeModel, Cmd.ofMsg msg)
            | ExecuteScripts(Finished(Ok resultDict)) ->
                let workerModel' =
                    { workerModel with
                        ScriptResultDict = Resolved resultDict }

                match resultDict with
                | dict when dict.Count > 0 ->
                    let note =
                        seq {
                            for k in dict.Keys do
                                match dict[k] with
                                | ExecuteResult.Output output ->
                                    for v in output do
                                        yield Client.Note.Text v
                                | ExecuteResult.Error ex -> yield Client.Note.Text(ex.ToString())
                        }
                        |> List.ofSeq

                    let clientModel' = { clientModel with Note = note }
                    let completeModel = (workerModel', clientModel')
                    let msg = ClientMsg Client.Msg.Send
                    (completeModel, Cmd.ofMsg msg)
                | _ ->
                    let completeModel = (workerModel', clientModel)
                    (completeModel, Cmd.none)
//| ConsumeScriptsCompleted ->
//    (workerModel, Cmd.none)
//    |> updateWith WorkerMsg model
// https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service
// New-Service -Name Xfinixy -BinaryPathName C:\Users\...\Xfixy.exe
