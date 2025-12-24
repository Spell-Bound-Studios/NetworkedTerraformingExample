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
        private Dictionary<int, VoxelEdit> _voxelEdits;
        
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
        /// Owner/Server broadcasts edits to all observers.
        /// bufferLast ensures late joiners get the most recent state.
        /// runLocally: false because the caller already applied locally.
        /// </summary>
        [ObserversRpc(bufferLast: true, runLocally: false)]
        private void BroadcastEdits(byte[] packedEdits) {
            var edits = Packer.UnpackListFromBytes<VoxelEdit>(packedEdits);
            ApplyLocally(edits);
        }

        private void ApplyLocally(List<VoxelEdit> edits) => OnEditsReceived?.Invoke(edits);
    }
}