using System;
using System.Collections.Generic;
using MMO.Net;
using UnityEngine;

namespace MMO.Persistence
{
    /// <summary>Pure Nakama login/zone payload builders and response parsers.</summary>
    internal static class NakamaApiPayloads
    {
        public static string BuildAuthenticateUrl(string baseUrl, string username)
            => $"{baseUrl}/v2/account/authenticate/email?create=true&username={Uri.EscapeDataString(username ?? string.Empty)}";

        public static string BuildAuthenticateJson(string email, string password)
            => JsonUtility.ToJson(new GatewayAccountEmailRequest { email = email, password = password });

        public static string BuildZoneReadJson(string userId)
            => JsonUtility.ToJson(new GatewayReadStorageObjectsRequest
            {
                objectIds = new List<GatewayReadStorageObjectId>
                {
                    new GatewayReadStorageObjectId
                    {
                        collection = NakamaApiGateway.LastZoneCollection,
                        key = NakamaApiGateway.LastZoneKey,
                        userId = userId,
                    },
                },
            });

        public static string BuildZoneWriteJson(LastZoneInfo zone)
            => JsonUtility.ToJson(new GatewayWriteStorageObjectsRequest
            {
                objects = new List<GatewayStorageObjectWrite>
                {
                    new GatewayStorageObjectWrite
                    {
                        collection = NakamaApiGateway.LastZoneCollection,
                        key = NakamaApiGateway.LastZoneKey,
                        value = JsonUtility.ToJson(zone),
                        version = string.Empty,
                        permissionRead = 1,
                        permissionWrite = 1,
                    },
                },
            });

        public static string ParseSessionToken(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonUtility.FromJson<GatewayLoginSession>(json).token;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        public static string ParseRefreshToken(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonUtility.FromJson<GatewayLoginSession>(json).refresh_token;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        public static TokenIdentity ParseAccountResponse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return default;

            try
            {
                GatewayAccountResponse response = JsonUtility.FromJson<GatewayAccountResponse>(json);
                if (response.user == null)
                    return default;

                return new TokenIdentity(response.user.id, response.user.username);
            }
            catch (ArgumentException)
            {
                return default;
            }
        }

        public static LastZoneInfo ParseZoneReadResponse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                GatewayStorageObjectsResponse response = JsonUtility.FromJson<GatewayStorageObjectsResponse>(json);
                if (response.objects == null || response.objects.Count == 0)
                    return null;

                GatewayStorageObjectValue obj = response.objects[0];
                if (string.IsNullOrWhiteSpace(obj.value))
                    return null;

                return LastZoneInfo.FromJson(obj.value);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        public static string ParseErrorMessage(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return null;

            try
            {
                return JsonUtility.FromJson<GatewayNakamaError>(responseBody).message;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
