module VisualInk.Server.DocStore

open Marten
open System.Linq
open Prelude
open Falco

let getStore = Handler.plug<IDocumentStore, _>

let queryShared<'a> logic =
  handler {
    let! docStore = getStore ()
    use session = docStore.QuerySession()
    let q: IQueryable<'a> = session.Query<'a>() |> logic

    return!
      q.ToListAsync()
      |> Handler.returnTask
      |> Handler.map Seq.toList
  }

let query<'a> logic =
  handler {
    let! user = User.ensureSessionUser()
    let! docStore = getStore ()
    let session = docStore.QuerySession(user.id.ToString())
    let q: IQueryable<'a> = session.Query<'a>() |> logic

    return!
      q.ToListAsync()
      |> Handler.returnTask
      |> Handler.map Seq.toList
  }

let loadShared<'a, 'id, 'error when 'a: not struct and 'a: not null> (entityId: 'id): Handler<'a option, 'error> =
  handler {
    let! docStore = getStore ()
    let session = docStore.QuerySession()
    return!
      session.LoadAsync<'a> entityId
      |> Handler.returnTask
      |> Handler.map Option.ofObj
  }
  
let load<'a, 'id, 'error when 'a: not struct and 'a: not null> (entityId: 'id): Handler<'a option, HttpHandler> =
  handler {
    let! docStore = getStore ()
    let! user = User.ensureSessionUser()
    let session = docStore.QuerySession(user.id.ToString())
    return!
      session.LoadAsync<'a> entityId
      |> Handler.returnTask
      |> Handler.map Option.ofObj
  }

let singleShared<'a, 'error when 'a: not struct and 'a: not null> logic: Handler<'a option, 'error> =
  handler {
    let! docStore = getStore ()
    let session = docStore.QuerySession()
    let q: IQueryable<'a> = session.Query<'a>() |> logic

    return!
      q.SingleOrDefaultAsync()
      |> Handler.returnTask
      |> Handler.map Option.ofObj
  }
  
let single<'a when 'a: not struct and 'a: not null> logic =
  handler {
    let! user = User.ensureSessionUser()
    let! docStore = getStore ()
    use session = docStore.QuerySession(user.id.ToString())
    let q: IQueryable<'a> = session.Query<'a>() |> logic

    return!
      q.SingleOrDefaultAsync()
      |> Handler.returnTask
      |> Handler.map Option.ofObj
  }

let startSession() =
    handler {
      let! docStore = getStore()
      let! user = User.ensureSessionUser()
      return docStore.LightweightSession(user.id.ToString())
    }

let startSharedSession() =
    handler {
      let! docStore = getStore()
      return docStore.LightweightSession()
    }

let saveChanges (session: IDocumentSession) =
    session.SaveChangesAsync() |> Handler.returnTask'
