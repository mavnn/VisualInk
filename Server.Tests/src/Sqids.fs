module Sqids

open Xunit
open VisualInk.Server.Prelude

[<Fact>]
let ``Sqids round trip from guid to sqid``() =
    let guid = System.Guid.NewGuid()
    let numbers = Slug.fromGuid guid
    let newGuid = Slug.toGuid numbers
    Assert.Equal(guid, newGuid)
