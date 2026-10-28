using System;
using System.Collections.Generic;

namespace MMO.Persistence
{
        [Serializable]
        internal class GatewayAccountEmailRequest
        {
            public string email;
            public string password;
        }

        [Serializable]
        internal class GatewayLoginSession
        {
            public string token;
            public string refresh_token;
        }

        [Serializable]
        internal class GatewayRefreshRequest
        {
            public string token;
        }

        [Serializable]
        internal class GatewayAccountResponse
        {
            public GatewayAccountUser user;
        }

        [Serializable]
        internal class GatewayAccountUser
        {
            public string id;
            public string username;
        }

        [Serializable]
        internal class GatewayNakamaError
        {
            public string message;
        }

        [Serializable]
        internal class GatewayReadStorageObjectsRequest
        {
            public List<GatewayReadStorageObjectId> objectIds;
        }

        [Serializable]
        internal class GatewayReadStorageObjectId
        {
            public string collection;
            public string key;
            public string userId;
        }

        [Serializable]
        internal class GatewayStorageObjectsResponse
        {
            public List<GatewayStorageObjectValue> objects;
        }

        [Serializable]
        internal class GatewayStorageObjectValue
        {
            public string value;
        }

        [Serializable]
        internal class GatewayWriteStorageObjectsRequest
        {
            public List<GatewayStorageObjectWrite> objects;
        }

        [Serializable]
        internal class GatewayStorageObjectWrite
        {
            public string collection;
            public string key;
            public string value;
            public string version;
            public int permissionRead;
            public int permissionWrite;
        }

        [Serializable]
        internal class GatewayWriteStorageObjectsResponse
        {
            public List<GatewayWriteStorageObjectAck> acks;
        }

        [Serializable]
        internal class GatewayWriteStorageObjectAck
        {
            public string version;
        }
}
