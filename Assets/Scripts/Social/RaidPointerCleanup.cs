using System;
using System.Threading.Tasks;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Social
{
    /// <summary>Best-effort cleanup of party raid pointers and orphaned raid records.</summary>
    internal sealed class RaidPointerCleanup
    {
        private readonly ISocialRepository _social;
        private readonly IRaidInstanceRepository _raids;
        private readonly int _casRetries;

        public RaidPointerCleanup(ISocialRepository social, IRaidInstanceRepository raids, int casRetries)
        {
            _social = social;
            _raids = raids;
            _casRetries = casRetries;
        }

        /// <summary>
        /// Clears one party's raid pointer, retrying on a version conflict. Best-effort: the
        /// reconcile pass is the backstop, so a failure is logged and never thrown.
        /// </summary>
        public async Task ClearAsync(string partyId)
        {
            for (int i = 0; i < _casRetries; i++)
            {
                try
                {
                    PartyRecord party = await _social.LoadPartyAsync(partyId);
                    if (party == null || string.IsNullOrEmpty(party.RaidInstanceId))
                        return;

                    party.RaidInstanceId = null;
                    await _social.SavePartyAsync(party);
                    return;
                }
                catch (StorageVersionConflictException)
                {
                    await RaidBackoff.DelayAsync(i);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Raid] Failed to clear raid pointer for party '{partyId}': {e.Message}");
                    return;
                }
            }
        }

        public async Task DeleteOrphanAsync(string instanceId)
        {
            try
            {
                await _raids.DeleteRaidInstanceAsync(instanceId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Raid] Failed to clean up orphan instance '{instanceId}': {e.Message}");
            }
        }
    }
}
