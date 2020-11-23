module Prelude

open System.Threading.Tasks
open FSharp.Control.Tasks

open System.Text.RegularExpressions

// Active pattern for regexes
let (|Regex|_|) pattern input =
  let m = Regex.Match(input, pattern)
  if m.Success then Some(List.tail [ for g in m.Groups -> g.Value ]) else None

let debug (msg : string) (a : 'a) : 'a =
  printfn $"DEBUG: {msg} ({a})"
  a

// Print the value of `a`. Note that since this is wrapped in a task, it must
// resolve the task before it can print, which could lead to different ordering
// of operations.
let debugTask (msg : string) (a : Task<'a>) : Task<'a> =
  task {
    let! a = a
    printfn $"DEBUG: {msg} ({a})"
    return a
  }

module String =
  // Returns a seq of EGC (extended grapheme cluster - essentially a visible
  // screen character)
  // https://stackoverflow.com/a/4556612/104021
  let toEgcSeq (s : string) : seq<string> =
    seq {
      let tee = System.Globalization.StringInfo.GetTextElementEnumerator(s)

      while tee.MoveNext() do
        yield tee.GetTextElement()
    }

  let lengthInEgcs (s : string) : int =
    System.Globalization.StringInfo(s).LengthInTextElements

  let toLower (str : string) : string = str.ToLower()

  let toUpper (str : string) : string = str.ToUpper()

open System.Threading.Tasks
open FSharp.Control.Tasks

type TaskOrValue<'T> =
  | Task of Task<'T>
  | Value of 'T

module TaskOrValue =
  // Wraps a value in TaskOrValue
  let unit v = Value v

  let toTask (v : TaskOrValue<'a>) : Task<'a> =
    task {
      match v with
      | Task t -> return! t
      | Value v -> return v
    }

  // Create a new TaskOrValue that first runs 'vt' and then
  // continues with whatever TaskorValue is produced by 'f'.
  let bind f vt =
    match vt with
    | Value v ->
        // It was a value, so we return 'f v' directly
        f v
    | Task t ->
        // It was a task, so we need to unwrap that and create
        // a new task - inside Task. If 'f v' returns a task, we
        // still need to return this as task though.
        Task
          (task {
            let! v = t

            match f v with
            | Value v -> return v
            | Task t -> return! t
           })

type TaskOrValueBuilder() =
  // This lets us use let!, do! - These can be overloaded
  // so I define two overloads for 'let!' - one taking regular
  // Task and one our TaskOrValue. You can then call both using
  // the let! syntax.
  member x.Bind(tv, f) = TaskOrValue.bind f tv
  member x.Bind(t, f) = TaskOrValue.bind f (Task t)
  // This lets us use return
  member x.Return(v) = TaskOrValue.unit (v)
  // This lets us use return!
  member x.ReturnFrom(tv) = tv
// To make this usable, this will need a few more
// especially for reasonable exception handling..

let taskv = TaskOrValueBuilder()

// Take a list of TaskOrValue and produce a single
// TaskOrValue that sequentially evaluates them and
// returns a list with results.
let collect list =
  let rec loop acc (xs : TaskOrValue<_> list) =
    taskv {
      match xs with
      | [] -> return List.rev acc
      | x :: xs ->
          let! v = x
          return! loop (v :: acc) xs
    }

  loop [] list

// Processes each item of the list in order, waiting for the previous one to
// finish. This ensures each request in the list is processed to completion
// before the next one is done, making sure that, for example, a HttpClient
// call will finish before the next one starts. Will allow other requests to
// run which waiting.
//
// Why can't this be done in a simple map? We need to resolve element i in
// element (i+1)'s task expression.
let map_s (f : 'a -> TaskOrValue<'b>) (list : List<'a>) : TaskOrValue<List<'b>> =
  taskv {
    let! result =
      match list with
      | [] -> taskv { return [] }
      | head :: tail ->
          taskv {
            let firstComp =
              taskv {
                let! result = f head
                return ([], result)
              }

            let! ((accum, lastcomp) : (List<'b> * 'b)) =
              List.fold (fun (prevcomp : TaskOrValue<List<'b> * 'b>) (arg : 'a) ->
                taskv {
                  // Ensure the previous computation is done first
                  let! ((accum, prev) : (List<'b> * 'b)) = prevcomp
                  let accum = prev :: accum

                  let! result = (f arg)

                  return (accum, result)
                }) firstComp tail

            return List.rev (lastcomp :: accum)
          }

    return (result |> Seq.toList)
  }
