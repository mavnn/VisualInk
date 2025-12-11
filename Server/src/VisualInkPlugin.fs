module VisualInk.Server.VisualInkPlugin

open Ink

type VisualInkPlugin () =
    interface IPlugin with
        member _.PreParse (storyContent: byref<string>): unit = 
            ()

        member _.PostExport (parsedStory: Parsed.Story, runtimeStory: byref<Runtime.Story>): unit = 
            ()

        member _.PostParse (parsedStory: byref<Parsed.Story>): unit = 
            ()
