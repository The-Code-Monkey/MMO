-- social_notify: the cross-zone fan-out hook for social state.
--
-- Every zone server's game socket has joined the room channel "room.system-social". A zone that
-- mutates shared state (a friend accept, a party change, chat bound for another zone) posts
-- /v2/rpc/social_notify with the bus envelope; this module rebuilds the same room channel id a
-- socket join would produce and relays the envelope to every joined socket in <100 ms.
--
-- None of this is authoritative. Delivery is best-effort by design: a dropped frame is repaired
-- by each zone's own 2 s reconcile pass re-reading versioned storage. That is why this module
-- never validates, orders or persists anything — it only relays.
local nk = require("nakama")

-- Must equal the exact target the client sends in channel_join
-- (SocialSocketProtocol.SocialRoom on the Unity side).
local ROOM = "room.system-social"

-- Rebroadcast a social notify onto the bus. The payload is already a bus envelope
-- ({"t":...,"target":...,"accounts":[...]}), so its fields are forwarded verbatim and every
-- zone's envelope handler sees the same shape it would for a chat frame.
local function social_notify(context, payload)
    local args = nk.json_decode(payload or "{}")

    -- Build the room channel id the same way a socket join would; never hard-code Nakama's
    -- internal "<mode>.<subject>.<subcontext>.<label>" form.
    local channel = nk.channel_id_build("", ROOM, 1)

    -- persist defaults to false: notify events never land in the message table. Before any zone
    -- has joined, the channel does not exist yet and the send fails — that is a harmless no-op,
    -- not an error, so it is swallowed rather than failing the RPC.
    local ok = pcall(function()
        nk.channel_message_send(channel, {
            t = args.t,               -- "friend" | "party" | "guild" | "presence"
            target = args.target,     -- optional party/guild id
            accounts = args.accounts, -- affected account ids; zones filter on these
        })
    end)

    return nk.json_encode({ ok = ok })
end

nk.register_rpc(social_notify, "social_notify")
