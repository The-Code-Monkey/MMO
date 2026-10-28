using System;
using System.Collections.Generic;

namespace MMO.Persistence
{
        [Serializable]
        internal class RestAccountDeviceRequest
        {
            public string id;
        }

        [Serializable]
        internal class RestAuthSession
        {
            public string token;

            // Nakama returns snake_case; JsonUtility is an exact match.
            public string refresh_token;
        }

        [Serializable]
        internal class RestRefreshRequest
        {
            public string token;
        }

        [Serializable]
        internal class RestRefreshResponse
        {
            public string token;
            public string refresh_token;
        }

        [Serializable]
        internal class RestRpcEnvelope
        {
            public string id;
            public string payload;
        }

        [Serializable]
        internal class RestAccountResponse
        {
            public RestAccountUser user;
        }

        [Serializable]
        internal class RestAccountUser
        {
            public string id;
        }

        [Serializable]
        internal class RestReadStorageObjectsRequest
        {
            public List<RestReadStorageObjectId> objectIds;
        }

        [Serializable]
        internal class RestReadStorageObjectId
        {
            public string collection;
            public string key;
            public string userId;
        }

        [Serializable]
        internal class RestStorageObjectsResponse
        {
            public List<RestStorageObject> objects;
        }

        [Serializable]
        internal class RestStorageObjectListResponse
        {
            public List<RestStorageObject> objects;
            public string cursor;
        }

        [Serializable]
        internal class RestStorageObject
        {
            public string collection;
            public string key;
            public string userId;
            public string value;
            public string version;
        }

        [Serializable]
        internal class RestWriteStorageObjectsRequest
        {
            public List<RestStorageObjectWrite> objects;
        }

        [Serializable]
        internal class RestStorageObjectWrite
        {
            public string collection;
            public string key;
            public string value;
            public string version;
            public int permissionRead;
            public int permissionWrite;
        }

        [Serializable]
        internal class RestWriteStorageObjectsResponse
        {
            public List<RestStorageObjectAck> acks;
        }

        [Serializable]
        internal class RestStorageObjectAck
        {
            public string version;
        }

        [Serializable]
        internal class RestDeleteStorageObjectsRequest
        {
            public List<RestDeleteStorageObjectId> objectIds;
        }

        [Serializable]
        internal class RestDeleteStorageObjectId
        {
            public string collection;
            public string key;
        }

        [Serializable]
        internal class RestEmptyResponse
        {
        }
}
