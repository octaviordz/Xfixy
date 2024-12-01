module Xfixy.Mantle.Setting

open Elmish
open System

type IOPath = System.IO.Path

type Model =
    { ScriptLocation: string
      AppDomainBaseDirectory: string
      CurrentDirectory: string }

type WorkerStatus =
    | Running
    | Stopped

type Msg =
    | UpdateWorkerStatus of WorkerStatus
    | StartWorker
    | StopWorker

let getScriptsLocation () =
    let localAppData =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)

    let fullPath = IOPath.Join(localAppData, "Xfixy", "Ps1-scripts")
    fullPath

let init () =
    { ScriptLocation = getScriptsLocation ()
      AppDomainBaseDirectory = AppDomain.CurrentDomain.BaseDirectory
      CurrentDirectory = Environment.CurrentDirectory },
    Cmd.ofMsg Msg.UpdateWorkerStatus

let update (msg: Msg) (model: Model) =
    match msg with
    | UpdateWorkerStatus newValue ->  Cmd.none, newValue
