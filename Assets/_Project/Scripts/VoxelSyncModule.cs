// Copyright 2025 Spellbound Studio Inc.

using System;
using System.Collections.Generic;
using PurrNet;
using Spellbound.Core.Packing;
using Spellbound.MarchingCubes;

namespace NetworkingMarchingCubes {
    /// <summary>
    /// NetworkModule responsible for synchronizing voxel edits across the network.
    /// </summary>
    [Serializable]
    public class VoxelSyncModule : NetworkModule {
        private readonly Dictionary<int, VoxelEdit> _voxelEdits;
        
        /// <summary>
        /// Invoked when edits should be applied locally.
        /// TestChunk subscribes to this and calls BaseChunk.ApplyVoxelEdits.
        /// </summary>
        public event Action<List<VoxelEdit>> OnEditsReceived;

        /// <summary>
        /// Called by TestChunk.PassVoxelEdits to route edits through the network.
        /// </summary>
        public void ProcessEdits(List<VoxelEdit> edits) {
            var packed = Packer.PackListToBytes(edits);

            if (isOwner || isServer) {
                ApplyLocally(edits);
                BroadcastEdits(packed);

                // Owner syncs to server for persistence
                if (isOwner && !isServer)
                    SyncEditsToServer(packed);
            }
            else
                RequestEdits(packed);
        }

        /// <summary>
        /// For clients that want to edit a chunk they don't own.
        /// Routes the request through the server/owner.
        /// </summary>
        [ServerRpc(requireOwnership: false)]
        public void RequestEdits(byte[] packedEdits, RPCInfo info = default) {
            var edits = Packer.UnpackListFromBytes<VoxelEdit>(packedEdits);
            ApplyLocally(edits);
            BroadcastEdits(packedEdits);
        }
        
        /// <summary>
        /// Owner syncs edits to server for persistence.
        /// Server stores but doesn't broadcast.
        /// </summary>
        [ServerRpc(requireOwnership: false)]
        private void SyncEditsToServer(byte[] packedEdits) {
            var edits = Packer.UnpackListFromBytes<VoxelEdit>(packedEdits);
            foreach (var edit in edits)
                _voxelEdits[edit.index] = edit;
        }

        /// <summary>
        /// Owner/Server broadcasts edits to all observers.
        /// bufferLast ensures late joiners get the most recent state.
        /// runLocally: false because the caller already applied locally.
        /// </summary>
        [ObserversRpc(bufferLast: true, runLocally: false)]
        private void BroadcastEdits(byte[] packedEdits) {
            var edits = Packer.UnpackListFromBytes<VoxelEdit>(packedEdits);
            ApplyLocally(edits);
        }

        private void ApplyLocally(List<VoxelEdit> edits) {
            foreach (var edit in edits)
                _voxelEdits[edit.index] = edit;

            OnEditsReceived?.Invoke(edits);
        }
        
        #region State Sync for New Observers

        /// <summary>
        /// Get packed edit state for sending to new observers.
        /// Called by TestChunk.OnObserverAdded.
        /// </summary>
        public byte[] GetPackedEditState() {
            var edits = new List<VoxelEdit>(_voxelEdits.Values);
            return edits.Count > 0
                    ? Packer.PackListToBytes(edits) 
                    : null;
        }

        /// <summary>
        /// Apply full edit state from server.
        /// Called after InitializeChunk to catch up new observers.
        /// </summary>
        public void ApplyFullEditState(byte[] packedEdits) {
            if (packedEdits == null || packedEdits.Length == 0)
                return;

            var edits = Packer.UnpackListFromBytes<VoxelEdit>(packedEdits);
            ApplyLocally(edits);
        }

        #endregion
    }
}