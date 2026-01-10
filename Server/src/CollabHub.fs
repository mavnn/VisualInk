module VisualInk.Server.CollabHub

open Microsoft.AspNetCore.SignalR

type IdProvider() =
  interface IUserIdProvider with
    member _.GetUserId context =
      if isNull context.User then
        null
      else
        let userId = context.User.FindFirst "userId"
        let connectionIdentifier = context.User.FindFirst "connectionIdentifier"
        match userId, connectionIdentifier with
        | null, null -> null
        | null, ci -> ci.Value
        | uuid, _ -> uuid.Value

type CollabHub() =
  inherit Hub()

  member x.CreateGroup() =
    task {
      let groupId = System.Guid.NewGuid().ToString()
      do! x.Groups.AddToGroupAsync(x.Context.ConnectionId, groupId)
      do! x.Groups.AddToGroupAsync(x.Context.ConnectionId, groupId + ":host")
      do! x.Clients.Client(x.Context.ConnectionId).SendAsync("GroupCreated", groupId)
    }

  member x.RequestDocument(groupName: string) =
    task {
      do! x.Groups.AddToGroupAsync(x.Context.ConnectionId, groupName)

      do!
        x.Clients
          .Group(groupName + ":host")
          .SendAsync("RequestDocument", x.Context.ConnectionId)
    }

  member x.DocumentRequested
    (
      connectionId: string,
      info: obj
    ) =
    x.Clients.Client(connectionId).SendAsync("DocumentRequested", info)

  member x.PushUpdates(groupName: string, version: int, updates: obj array) =
    x.Clients.Group(groupName + ":host").SendAsync("PushUpdates", version, updates)

  member x.UpdateBroadcast(groupName: string, version: int, update: obj) =
    x.Clients.Group(groupName).SendAsync("UpdateBroadcast", version, update)
