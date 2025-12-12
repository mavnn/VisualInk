module VisualInk.Server.VisualInkPlugin

open Ink
open System.Text.RegularExpressions

let private StackDefinitionRegex =
  Regex(
    "^STACK\s+(?<label>\w+)\s+(?<size>\d+)\s+(?<nil>.*)$",
    RegexOptions.Multiline ||| RegexOptions.Compiled
  )

let private StackIncludeRegex =
  Regex(
    "^VisualInkStack (?<label>\w+) (?<size>\d+) (?<nil>.*)$",
    RegexOptions.Multiline ||| RegexOptions.Compiled
  )

type PluginInclude =
    | StackInclude of string * int * string

type VisualInkPlugin() =
  interface IPlugin with
    member _.PreParse(storyContent: byref<string>) : unit =
      storyContent <-
        StackDefinitionRegex.Replace(
          storyContent,
          "INCLUDE VisualInkStack ${label} ${size} ${nil}"
        )

    member _.PostExport
      (parsedStory: Parsed.Story, runtimeStory: byref<Runtime.Story>)
      : unit =
      ()

    member _.PostParse(parsedStory: byref<Parsed.Story>) : unit = ()

let isPluginInclude fileName =
    let m = StackIncludeRegex.Match fileName
    if m.Success then
        StackInclude (m.Groups.["label"].Value, System.Int32.Parse m.Groups.["size"].Value, m.Groups.["nil"].Value)
        |> Some
    else None

let generatePluginInclude (StackInclude (label, size, nil)) =
    let last = $"{label}_last"
    let content = seq {
      // Set up storage
      yield $"VAR {label}_size = {size}"
      yield $"VAR {last} = 0"
      for i in 1 .. size do
        yield $"VAR {label}_{i} = {nil}"
      yield ""

      // Access via index
      yield $"=== function {label}_index(index)"
      yield "{ index:"
      for i in 1 .. size do
        yield $"  - {i}:"
        yield $"    ~return {label}_{i}"
      yield "}"
      yield ""

      // Set via index
      yield $"=== function {label}_set(index, value)"
      yield "{ index:"
      for i in 1 .. size do
        yield $"  - {i}:"
        yield $"    ~{label}_{i} = value"
      yield "}"
      yield ""

      // Push
      yield $"=== function {label}_push(value)"
      yield $"{{ {last} >= {size}:"
      yield "  ~return false"
      yield "- else:"
      yield $"  ~{last}++"
      yield $"  ~{label}_set({last}, value)"
      yield "  ~return true"
      yield "}"

      // Pop
      yield $"=== function {label}_pop()"
      yield $"{{ {last} <= 0:"
      yield $"  ~return {nil}"
      yield "- else:"
      yield $"  ~temp result = {label}_index({last})"
      yield $"  ~{label}_set({last}, {nil})"
      yield $"  ~{last}--"
      yield "  ~return result"
      yield "}"
    }
    content |> String.concat "\n"
