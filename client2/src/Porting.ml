type size = { width : int; height : int }
module Native = struct
  module Window = struct
    let window : Dom.window = [%bs.raw "window"]
    external getWidth : Dom.window -> int = "innerWidth" [@@bs.get]
    external getHeight : Dom.window -> int = "innerHeight" [@@bs.get]
    let size () : size =
      { width = getWidth window
      ; height = getHeight window
      }
  end

  module Random = struct
    let random () : int = Random.int 2147483647
  end
end

let (++) (a: string) (b: string) = a ^ b

module String = struct
  include String
  let toInt (s: string) : int = int_of_string s
  let toFloat (s: string) : float = float_of_string s
  let uncons (s: string) : (char * string) option =
    match s with
    | "" -> None
    | s -> Some (String.get s 0, String.sub s 1 (String.length s - 1))
end

module Option = struct
  include Belt.Option
  let andThen (o: 'a option) (fn: 'a -> 'b option) : 'b option =
    match o with
    | None -> None
    | Some x -> fn x
  let orElse  (ma : 'a option) (mb: 'a option) : ('a option) =
    match mb with
    | None -> ma
    | Some _ -> mb
end

module Result = struct
  include Belt.Result
  let withDefault (r: ('a, 'b) t) (default: 'a) : 'a =
    getWithDefault r default
end
type ('a, 'b) result = ('a, 'b) Result.t

module Regex = struct
  let regex s : Js.Re.t = Js.Re.fromString ("/" ^ s ^ "/")
  let contains (re: Js.Re.t) (s: string) : bool = Js.Re.test s re
  let replace (re: string) (repl: string) (str: string) =
    Js.String.replaceByRe (regex re) repl str
end

let to_option (value: 'a) (sentinel: 'a) : 'a option =
  if value = sentinel
  then None
  else Some value


module List = struct
  include Belt.List
  let indexedMap fn l = mapWithIndex l fn
  let map2 (fn: 'a -> 'b -> 'c) (a: 'a list) (b: 'b list) : 'c list =
    mapReverse2 a b fn |> reverse
  let getBy fn l = getBy l fn
  let elemIndex (a: 'a) (l : 'a list) : int option =
    l
    |> Array.of_list
    |> Js.Array.findIndex ((=) a)
    |> to_option (-1)
  let rec last (l : 'a list) : 'a option =
    match l with
    | [] -> None
    | [a] -> Some a
    | _ :: tail -> last tail

end

module Char = struct
  include Char
  let toCode (c: char) : int = code c
  let fromCode (i: int) : char = chr i
end

module Tuple2 = struct
  let create a b = (a,b)
end



