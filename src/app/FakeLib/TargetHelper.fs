﻿[<AutoOpen>]
/// Contains infrastructure code and helper functions for FAKE's target feature.
module Fake.TargetHelper

open System
open System.Collections.Generic
open System.Linq

/// [omit]
type TargetDescription = string

/// [omit]
type 'a TargetTemplate =
    { Name: string;
      Dependencies: string list;
      SoftDependencies: string list;
      Description: TargetDescription;
      Function : 'a -> unit}

/// A Target can be run during the build
type Target = unit TargetTemplate

type private DependencyType =
    | Hard = 1
    | Soft = 2

type private DependencyLevel =
    {
        level:int;
        dependants: string list;
    }

/// [omit]
let mutable PrintStackTraceOnError = false

/// [omit]
let mutable LastDescription = null

/// Sets the Description for the next target.
/// [omit]
let Description text =
    if LastDescription <> null then
        failwithf "You can't set the description for a target twice. There is already a description: %A" LastDescription
    LastDescription <- text

/// TargetDictionary
/// [omit]
let TargetDict = new Dictionary<_,_>(StringComparer.OrdinalIgnoreCase)

/// Final Targets - stores final targets and if they are activated.
let FinalTargets = new Dictionary<_,_>(StringComparer.OrdinalIgnoreCase)

/// BuildFailureTargets - stores build failure targets and if they are activated.
let BuildFailureTargets = new Dictionary<_,_>(StringComparer.OrdinalIgnoreCase)

/// The executed targets.
let ExecutedTargets = new HashSet<_>(StringComparer.OrdinalIgnoreCase)

/// The executed target time.
/// [omit]
let ExecutedTargetTimes = new List<_>()

/// Resets the state so that a deployment can be invoked multiple times
/// [omit]
let reset() =
    TargetDict.Clear()
    ExecutedTargets.Clear()
    BuildFailureTargets.Clear()
    ExecutedTargetTimes.Clear()
    FinalTargets.Clear()

let mutable CurrentTargetOrder = []
let mutable CurrentTarget = ""

/// Returns a list with all target names.
let getAllTargetsNames() = TargetDict |> Seq.map (fun t -> t.Key) |> Seq.toList

/// Gets a target with the given name from the target dictionary.
let getTarget name =
    match TargetDict.TryGetValue (name) with
    | true, target -> target
    | _  ->
        traceError <| sprintf "Target \"%s\" is not defined. Existing targets:" name
        for target in TargetDict do
            traceError  <| sprintf "  - %s" target.Value.Name
        failwithf "Target \"%s\" is not defined." name

/// Returns the DependencyString for the given target.
let dependencyString target =
    if target.Dependencies.IsEmpty then String.Empty else
    target.Dependencies
      |> Seq.map (fun d -> (getTarget d).Name)
      |> separated ", "
      |> sprintf "(==> %s)"

/// Returns the soft  DependencyString for the given target.
let softDependencyString target =
    if target.SoftDependencies.IsEmpty then String.Empty else
    target.SoftDependencies
      |> Seq.map (fun d -> (getTarget d).Name)
      |> separated ", "
      |> sprintf "(?=> %s)"

/// Do nothing - fun () -> () - Can be used to define empty targets.
let DoNothing = (fun () -> ())

/// Checks whether the dependency (soft or normal) can be added.
/// [omit]
let checkIfDependencyCanBeAddedCore fGetDependencies targetName dependentTargetName =
    let target = getTarget targetName
    let dependentTarget = getTarget dependentTargetName

    let rec checkDependencies dependentTarget =
          fGetDependencies dependentTarget
          |> List.iter (fun dep ->
               if toLower dep = toLower targetName then
                  failwithf "Cyclic dependency between %s and %s" targetName dependentTarget.Name
               checkDependencies (getTarget dep))

    checkDependencies dependentTarget
    target,dependentTarget

/// Checks whether the dependency can be added.
/// [omit]
let checkIfDependencyCanBeAdded targetName dependentTargetName =
   checkIfDependencyCanBeAddedCore (fun target -> target.Dependencies) targetName dependentTargetName

/// Checks whether the soft dependency can be added.
/// [omit]
let checkIfSoftDependencyCanBeAdded targetName dependentTargetName =
   checkIfDependencyCanBeAddedCore (fun target -> target.SoftDependencies) targetName dependentTargetName

/// Adds the dependency to the front of the list of dependencies.
/// [omit]
let dependencyAtFront targetName dependentTargetName =
    let target,dependentTarget = checkIfDependencyCanBeAdded targetName dependentTargetName

    TargetDict.[targetName] <- { target with Dependencies = dependentTargetName :: target.Dependencies }

/// Appends the dependency to the list of dependencies.
/// [omit]
let dependencyAtEnd targetName dependentTargetName =
    let target,dependentTarget = checkIfDependencyCanBeAdded targetName dependentTargetName

    TargetDict.[targetName] <- { target with Dependencies = target.Dependencies @ [dependentTargetName] }


/// Appends the dependency to the list of soft dependencies.
/// [omit]
let softDependencyAtEnd targetName dependentTargetName =
    let target,dependentTarget = checkIfDependencyCanBeAdded targetName dependentTargetName

    TargetDict.[targetName] <- { target with SoftDependencies = target.SoftDependencies @ [dependentTargetName] }

/// Adds the dependency to the list of dependencies.
/// [omit]
let dependency targetName dependentTargetName = dependencyAtEnd targetName dependentTargetName

/// Adds the dependency to the list of soft dependencies.
/// [omit]
let softDependency targetName dependentTargetName = softDependencyAtEnd targetName dependentTargetName

/// Adds the dependencies to the list of dependencies.
/// [omit]
let Dependencies targetName dependentTargetNames = dependentTargetNames |> List.iter (dependency targetName)

/// Adds the dependencies to the list of soft dependencies.
/// [omit]
let SoftDependencies targetName dependentTargetNames = dependentTargetNames |> List.iter (softDependency targetName)

/// Backwards dependencies operator - x is dependent on ys.
let inline (<==) x ys = Dependencies x ys

/// Set a dependency for all given targets.
/// [omit]
[<Obsolete("Please use the ==> operator")>]
let TargetsDependOn target targets =
    getAllTargetsNames()
    |> Seq.toList  // work on copy since the dict will be changed
    |> List.filter ((<>) target)
    |> List.filter (fun t -> Seq.exists ((=) t) targets)
    |> List.iter (fun t -> dependencyAtFront t target)

/// Set a dependency for all registered targets.
/// [omit]
[<Obsolete("Please use the ==> operator")>]
let AllTargetsDependOn target =
    let targets = getAllTargetsNames()

    targets
    |> Seq.toList  // work on copy since the dict will be changed
    |> List.filter ((<>) target)
    |> List.filter (fun t -> Seq.exists ((=) t) targets)
    |> List.iter (fun t -> dependencyAtFront t target)

/// Creates a target from template.
/// [omit]
let targetFromTemplate template name parameters =
    TargetDict.Add(name,
      { Name = name;
        Dependencies = [];
        SoftDependencies = [];
        Description = template.Description;
        Function = fun () ->
          // Don't run function now
          template.Function parameters })

    name <== template.Dependencies
    LastDescription <- null

/// Creates a TargetTemplate with dependencies.
///
/// ## Sample
///
/// The following sample creates 4 targets using TargetTemplateWithDependencies and hooks them into the build pipeline.
///
///     // Create target creation functions
///     let createCompileTarget name strategy =
///     TargetTemplateWithDependencies
///         ["Clean"; "ResolveDependencies"] // dependencies to other targets
///         (fun targetParameter ->
///           tracefn "--- start compile product..."
///           if targetParameter = "a" then
///             tracefn "    ---- Strategy A"
///           else
///             tracefn "    ---- Strategy B"
///           tracefn "--- finish compile product ..."
///         ) name strategy
///
///     let createTestTarget name dependencies filePattern =
///       TargetTemplateWithDependencies
///         dependencies
///         (fun filePattern ->
///           tracefn "--- start compile tests ..."
///           !! filePattern
///           |> RunTests
///           tracefn "--- finish compile tests ...")
///         name filePattern
///
///     // create some targets
///     createCompileTarget "C1" "a"
///     createCompileTarget "C2" "b"
///
///     createTestTarget "T1" ["C1"] "**/C1/*.*"
///     createTestTarget "T2" ["C1"; "C2"] "**/C?/*.*"
///
///     // hook targets to normal build pipeline
///     "T1" ==> "T2" ==> "Test"
///
let TargetTemplateWithDependencies dependencies body name parameters =
    let template =
        { Name = String.Empty
          Dependencies = dependencies
          SoftDependencies = []
          Description = LastDescription
          Function = body }
    targetFromTemplate template name parameters

[<Obsolete("Use TargetTemplateWithDependencies")>]
let TargetTemplateWithDependecies dependencies = TargetTemplateWithDependencies dependencies

/// Creates a TargetTemplate.
let TargetTemplate body = TargetTemplateWithDependencies [] body

/// Creates a Target.
let Target name body = TargetTemplate body name ()

/// Represents build errors
type BuildError = {
    Target : string
    Message : string }

let mutable private errors = []

/// Get Errors - Returns the errors that occured during execution
let GetErrors() = errors

/// [omit]
let targetError targetName (exn:System.Exception) =
    closeAllOpenTags()
    errors <-
        match exn with
            | BuildException(msg, errs) ->
                let errMsgs = errs |> List.map(fun e -> { Target = targetName; Message = e })
                { Target = targetName; Message = msg } :: (errMsgs @ errors)
            | _ -> { Target = targetName; Message = exn.ToString() } :: errors
    let error e =
        match e with
        | BuildException(msg, errs) -> msg + (if PrintStackTraceOnError then Environment.NewLine + e.StackTrace.ToString() else "")
        | _ ->
            if exn :? FAKEException then
                exn.Message
            else
                exn.ToString()


    let msg = sprintf "%s%s" (error exn) (if exn.InnerException <> null then "\n" + (exn.InnerException |> error) else "")
    traceError <| sprintf "Running build failed.\nError:\n%s" msg

    let isFailedTestsException = exn :? UnitTestCommon.FailedTestsException
    if not isFailedTestsException  then
        sendTeamCityError <| error exn

let addExecutedTarget target time =
    lock ExecutedTargets (fun () ->
        ExecutedTargets.Add (target) |> ignore
        ExecutedTargetTimes.Add(target,time) |> ignore
    )

/// Runs all activated final targets (in alphabetically order).
/// [omit]
let runFinalTargets() =
    FinalTargets
      |> Seq.filter (fun kv -> kv.Value)     // only if activated
      |> Seq.map (fun kv -> kv.Key)
      |> Seq.iter (fun name ->
           try
               let watch = new System.Diagnostics.Stopwatch()
               watch.Start()
               tracefn "Starting FinalTarget: %s" name
               (getTarget name).Function()
               addExecutedTarget name watch.Elapsed
           with
           | exn -> targetError name exn)

/// Runs all build failure targets.
/// [omit]
let runBuildFailureTargets() =
    BuildFailureTargets
      |> Seq.filter (fun kv -> kv.Value)     // only if activated
      |> Seq.map (fun kv -> kv.Key)
      |> Seq.iter (fun name ->
           try
               let watch = new System.Diagnostics.Stopwatch()
               watch.Start()
               tracefn "Starting BuildFailureTarget: %s" name
               (getTarget name).Function()
               addExecutedTarget name watch.Elapsed
           with
           | exn -> targetError name exn)


/// Prints all targets.
let PrintTargets() =
    log "The following targets are available:"
    for t in TargetDict.Values do
        logfn "   %s%s" t.Name (if isNullOrEmpty t.Description then "" else sprintf " - %s" t.Description)

// Maps the specified dependency type into the list of targets
let private withDependencyType (depType:DependencyType) targets =
    targets |> List.map (fun t -> depType, t)

// Helper function for visiting targets in a dependency tree. Returns a set containing the names of the all the
// visited targets, and a list containing the targets visited ordered such that dependencies of a target appear earlier
// in the list than the target.
let private visitDependencies fVisit targetName =
    let visit fGetDependencies fVisit targetName =
        let visited = new HashSet<_>()
        let ordered = new List<_>()
        let rec visitDependenciesAux level (dependentTarget:option<TargetTemplate<unit>>) (depType,targetName) =
            let target = getTarget targetName
            let isVisited = visited.Contains targetName
            visited.Add targetName |> ignore
            fVisit (dependentTarget, target, depType, level, isVisited)
            
            (fGetDependencies target) |> Seq.iter (visitDependenciesAux (level + 1) (Some target))                
            
            if not isVisited then ordered.Add targetName
        visitDependenciesAux 0 None (DependencyType.Hard, targetName)
        visited, ordered

    // First pass is to accumulate targets in (hard) dependency graph
    let visited, _ = visit (fun t -> t.Dependencies |> withDependencyType DependencyType.Hard) (fun _ -> ()) targetName

    let getAllDependencies (t: TargetTemplate<unit>) =
         (t.Dependencies |> withDependencyType DependencyType.Hard) @
         // Note that we only include the soft dependency if it is present in the set of targets that were
         // visited.
         (t.SoftDependencies |> List.filter visited.Contains |> withDependencyType DependencyType.Soft)

    // Now make second pass, adding in soft depencencies if appropriate
    visit getAllDependencies fVisit targetName
    
/// <summary>Writes a dependency graph.</summary>
/// <param name="verbose">Whether to print verbose output or not.</param>
/// <param name="target">The target for which the dependencies should be printed.</param>
let PrintDependencyGraph verbose target =
    match TargetDict.TryGetValue (target) with
    | false,_ -> PrintTargets()
    | true,target ->
        logfn "%sDependencyGraph for Target %s:" (if verbose then String.Empty else "Shortened ") target.Name

        let logDependency (_, (t: TargetTemplate<unit>), depType, level, isVisited) =
            if verbose ||  not isVisited then
                let indent = (String(' ', level * 3))
                if depType = DependencyType.Soft then
                    log <| sprintf "%s<=? %s" indent t.Name
                else
                    log <| sprintf "%s<== %s" indent t.Name

        let _, ordered = visitDependencies logDependency target.Name
        log ""

let PrintRunningOrder() = 
        log "The running order is:"
        CurrentTargetOrder
        |> List.iteri (fun index x ->  
                                if (environVarOrDefault "parallel-jobs" "1" |> int > 1) then                               
                                    logfn "Group - %d" (index + 1)
                                Seq.iter (logfn "  - %s") x)

/// <summary>Writes a dependency graph of all targets in the DOT format.</summary>
let PrintDotDependencyGraph () =
    logfn "digraph G {"
    logfn "  rankdir=TB;"
    logfn "  node [shape=box];"
    for target in TargetDict.Values do
        logfn "  \"%s\";" target.Name
        let deps = target.Dependencies
        for d in target.Dependencies do
            logfn "  \"%s\" -> \"%s\"" target.Name d
        for d in target.SoftDependencies do
            logfn "  \"%s\" -> \"%s\" [style=dotted];" target.Name d
    logfn "}"

/// Writes a summary of errors reported during build.
let WriteErrors () =
    traceLine()
    errors
    |> Seq.mapi(fun i e -> sprintf "%3d) %s" (i + 1) e.Message)
    |> Seq.iter(fun s -> traceError s)

/// <summary>Writes a build time report.</summary>
/// <param name="total">The total runtime.</param>
let WriteTaskTimeSummary total =
    traceHeader "Build Time Report"

    let width = ExecutedTargetTimes
                |> Seq.map (fun (a,b) -> a.Length)
                |> Seq.append([CurrentTarget.Length])
                |> Seq.max
                |> max 8

    let aligned (name:string) duration = tracefn "%s   %O" (name.PadRight width) duration
    let alignedError (name:string) duration = sprintf "%s   %O" (name.PadRight width) duration |> traceError

    aligned "Target" "Duration"
    aligned "------" "--------"

    ExecutedTargetTimes
        |> Seq.iter (fun (name,time) ->
            let t = getTarget name
            aligned t.Name time)
        
    if errors = [] && ExecutedTargetTimes.Count > 0 then 
        aligned "Total:" total
        traceLine()
        aligned "Status:" "Ok"
    else if ExecutedTargetTimes.Count > 0 then
        let failedTarget = getTarget CurrentTarget
        alignedError failedTarget.Name "Failure"
        aligned "Total:" total
        traceLine()
        alignedError "Status:" "Failure"
        traceLine()
        WriteErrors()
    else
        let failedTarget = getTarget CurrentTarget
        alignedError failedTarget.Name "Failure"
        traceLine()
        alignedError "Status:" "Failure"

    traceLine()

module ExitCode =
    let exitCode = ref 0
    let mutable Value = 42
let private changeExitCodeIfErrorOccured() = if errors <> [] then Environment.ExitCode <- ExitCode.Value; ExitCode.exitCode := ExitCode.Value

/// [omit]
let isListMode = hasBuildParam "list"

/// Prints all available targets.
let listTargets() =
    tracefn "Available targets:"
    TargetDict.Values
      |> Seq.iter (fun target ->
            tracefn "  - %s %s" target.Name (if target.Description <> null then " - " + target.Description else "")
            tracefn "     Depends on: %A" target.Dependencies)

// Instead of the target can be used the list dependencies graph parameter.
let doesTargetMeanListTargets target = target = "--listTargets"  || target = "-lt"

/// <summary>
/// Gets a flag indicating that the user requested to output a DOT-graph
/// of target dependencies instead of building a target.
///</summary>
let private doesTargetMeanPrintDotGraph target = target = "--dotGraph"  || target = "-dg"

/// Determines a parallel build order for the given set of targets
let determineBuildOrder (target : string) =

    let t = getTarget target

    let targetLevels = new Dictionary<string,DependencyLevel>()

    let appendDepentantOption (currentList:string list) (dependantTarget:option<TargetTemplate<unit>>) = 
        match dependantTarget with
        | None -> currentList
        | Some x -> List.append currentList [x.Name] |> List.distinct 

    let SetDependency dependantTarget target = 
        match targetLevels.TryGetValue target with
        | true, exDependencyLevel -> 
            targetLevels.[target] <- {level = exDependencyLevel.level; dependants = (appendDepentantOption exDependencyLevel.dependants dependantTarget)}
        | _ -> ()

    let rec SetTargetLevel newLevel target  = 
        match targetLevels.TryGetValue target with
        | true, exDependencyLevel -> 
            let minLevel = targetLevels
                           |> Seq.filter(fun x -> x.Value.dependants.Contains target)
                           |> Seq.map(fun x -> x.Value.level)
                           |> fun x -> match x.Any() with
                                       | true -> x |> Seq.min
                                       | _ -> -1
            
            if exDependencyLevel.dependants.Length > 0 then
                if (exDependencyLevel.level < newLevel && (newLevel < minLevel || minLevel = -1)) || (exDependencyLevel.level > newLevel) then
                    targetLevels.[target] <- {level = newLevel; dependants = exDependencyLevel.dependants}
                if exDependencyLevel.level < newLevel then
                    exDependencyLevel.dependants |> List.iter (fun x -> SetTargetLevel (newLevel - 1) x)
        | _ -> ()

    let AddNewTargetLevel dependantTarget level target =
        targetLevels.[target] <- {level = level; dependants=(appendDepentantOption [] dependantTarget)}
        
    let addTargetLevel ((dependantTarget:option<TargetTemplate<unit>>), (target: TargetTemplate<unit>), _, level, _ ) =
        let (|LevelIncreaseWithDependantTarget|_|) = function
        | (true, exDependencyLevel), Some dt when exDependencyLevel.level > level -> Some (exDependencyLevel, dt)
        | _ -> None

        let (|LevelIncreaseWithNoDependantTarget|_|) = function
        | (true, exDependencyLevel), None when exDependencyLevel.level > level -> Some (exDependencyLevel)
        | _ -> None
        
        let (|LevelDecrease|_|) = function
        | (true, exDependencyLevel), _ when exDependencyLevel.level < level -> Some (exDependencyLevel)
        | _ -> None

        let (|AddDependency|_|) = function
        | (true, exDependencyLevel), Some dt when not(exDependencyLevel.dependants.Contains dt.Name) -> Some (exDependencyLevel, dt)
        | _ -> None

        let (|NewTarget|_|) = function
        | (false, _), _ -> Some ()
        | _ -> None

        match targetLevels.TryGetValue target.Name, dependantTarget with
        | LevelIncreaseWithDependantTarget (exDependencyLevel, dt) ->
            SetDependency dependantTarget target.Name
            SetTargetLevel (exDependencyLevel.level - 1) dt.Name
        |  LevelIncreaseWithNoDependantTarget (exDependencyLevel) -> 
            SetDependency dependantTarget target.Name
        |  LevelDecrease (exDependencyLevel) -> 
            SetDependency dependantTarget target.Name
            SetTargetLevel level target.Name
        |  AddDependency (exDependencyLevel, dt) -> 
            SetDependency dependantTarget target.Name
        | NewTarget -> 
            AddNewTargetLevel dependantTarget level target.Name
        | _ -> ()

    visitDependencies addTargetLevel target |> ignore

    // the results are grouped by their level, sorted descending (by level) and
    // finally grouped together in a list<TargetTemplate<unit>[]
    targetLevels
    |> Seq.map (fun pair -> pair.Key, pair.Value.level)
    |> Seq.groupBy snd
    |> Seq.sortBy (fun (l,_) -> -l)
    |> Seq.map snd
    |> Seq.map (fun v -> v |> Seq.map fst |> Seq.distinct |> Seq.map getTarget |> Seq.toArray)
    |> Seq.toList

/// Runs a single target without its dependencies
let runSingleTarget (target : TargetTemplate<unit>) =
    try
        if errors = [] then
            traceStartTarget target.Name target.Description (dependencyString target)
            CurrentTarget <- target.Name
            let watch = new System.Diagnostics.Stopwatch()
            watch.Start()
            target.Function()
            addExecutedTarget target.Name watch.Elapsed
            traceEndTarget target.Name
            CurrentTarget <- ""
    with exn ->
        targetError target.Name exn

/// Runs the given array of targets in parallel using count tasks
let runTargetsParallel (count : int) (targets : Target[]) =
    targets.AsParallel()
        .WithDegreeOfParallelism(count)
        .Select(runSingleTarget)
        .ToArray()
    |> ignore
    
let runTargets (targets: TargetTemplate<unit> array) =
    let lastTarget = targets |> Array.last
    if errors = [] && ExecutedTargets.Contains (lastTarget.Name) |> not then
        let firstTarget = targets |> Array.head
        if hasBuildParam "single-target" then
            traceImportant "Single target mode ==> Skipping dependencies."
            runSingleTarget lastTarget
        else
            targets |> Array.iter runSingleTarget

/// Runs a target and its dependencies.
let run targetName =
    if doesTargetMeanPrintDotGraph targetName then PrintDotDependencyGraph() else
    if doesTargetMeanListTargets targetName then listTargets() else
    if LastDescription <> null then failwithf "You set a task description (%A) but didn't specify a task." LastDescription
    
    let watch = new System.Diagnostics.Stopwatch()
    watch.Start()
    try
        tracefn "Building project with version: %s" buildVersion
        PrintDependencyGraph false targetName

        // determine a parallel build order
        let order = determineBuildOrder targetName

        let parallelJobs = environVarOrDefault "parallel-jobs" "1" |> int

        // Figure out the order in in which targets can be run, and which can be run in parallel.
        if parallelJobs > 1 then
            tracefn "Running parallel build with %d workers" parallelJobs
            CurrentTargetOrder <- order |> List.map (fun targets -> targets |> Array.map (fun t -> t.Name) |> Array.toList)

            PrintRunningOrder()

            // run every level in parallel
            for par in order do
                runTargetsParallel parallelJobs par
        else
            
            let flatenedOrder = order |> List.map (fun targets -> targets |> Array.map (fun t -> t.Name)  |> Array.toList) |> List.concat
            CurrentTargetOrder <- flatenedOrder |> Seq.map (fun t -> [t]) |> Seq.toList

            PrintRunningOrder()

            runTargets (flatenedOrder |> Seq.map getTarget |> Seq.toArray)

    finally
        if errors <> [] then
            runBuildFailureTargets()
        runFinalTargets()
        killAllCreatedProcesses()
        WriteTaskTimeSummary watch.Elapsed
        changeExitCodeIfErrorOccured()

/// Registers a BuildFailureTarget (not activated).
let BuildFailureTarget name body =
    Target name body
    BuildFailureTargets.Add(name,false)

/// Activates the BuildFailureTarget.
let ActivateBuildFailureTarget name =
    let t = getTarget name // test if target is defined
    BuildFailureTargets.[name] <- true

/// Registers a final target (not activated).
let FinalTarget name body =
    Target name body
    FinalTargets.Add(name,false)

/// Activates the FinalTarget.
let ActivateFinalTarget name =
    let t = getTarget name // test if target is defined
    FinalTargets.[name] <- true
