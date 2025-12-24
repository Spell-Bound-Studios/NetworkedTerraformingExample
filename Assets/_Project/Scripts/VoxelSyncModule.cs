// Copyright 2025 Spellbound Studio Inc.

using System;
using System.Collections.Generic;
using System.Linq;
using PurrNet;
using PurrNet.Transports;
using Spellbound.Core.Packing;
using Spellbound.MarchingCubes;
using Unity.VisualScripting;
using UnityEngine;

namespace NetworkingMarchingCubes {
    /// <summary>
    /// NetworkModule responsible for synchronizing voxel edits across the network.
    /// </summary>
    [Serializable]
    public class VoxelSyncModule : NetworkModule {
        private readonly Dictionary<int, VoxelEdit> _voxelEdits = new();
        
        /// <summary>
        /// Invoked when edits should be applied locally.
        /// TestChunk subscribes to this and calls BaseChunk.ApplyVoxelEdits.
        /// </summary>
        public event Action<List<VoxelEdit>> onVoxelsChanged;

        public VoxelSyncModule() {
            Debug.Log("VoxelSyncModule constructor is running");
            if (!isServer)
                return;
            
            onVoxelsChanged += BookeepVoxelEdits;

        }

        public override void OnPoolReset() {
            base.OnPoolReset();
            onVoxelsChanged -= BookeepVoxelEdits;
        } 

        private void BookeepVoxelEdits(List<VoxelEdit> newEdits) {
            foreach (var edit in newEdits) {
                _voxelEdits[edit.index] = edit;
            }
        }

        /// <summary>
        /// Called by TestChunk.PassVoxelEdits to route edits through the network.
        /// </summary>
        public void ProcessEdits(List<VoxelEdit> edits) {
            var packed = Packer.PackListToBytes(edits);

            if (isOwner || isServer) {
                onVoxelsChanged?.Invoke(edits);
                HandleStateChangeORPC(packed);
            }
            else
                HandleClientTryChangeSRPC(packed);
        }

        public override void OnObserverAdded(PlayerID player) {
            base.OnObserverAdded(player);
            var packed = Packer.PackListToBytes(_voxelEdits.Values.ToList());
            HandleInitialStateTRPC(player, packed);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true)]
        private void HandleStateChangeORPC(byte[] batchedOfPackedEdits) {
            var edits = Packer.UnpackListFromBytes<VoxelEdit>(batchedOfPackedEdits);
            onVoxelsChanged?.Invoke(edits);
        }

        [TargetRpc(Channel.ReliableOrdered)]
        private void HandleInitialStateTRPC(PlayerID player, byte[] allEditsPacked) {
            var edits = Packer.UnpackListFromBytes<VoxelEdit>(allEditsPacked);
            onVoxelsChanged?.Invoke(edits);
        }
        

        /// <summary>
        /// For clients that want to edit a chunk they don't own.
        /// Routes the request through the server/owner.
        /// </summary>
        [ServerRpc(requireOwnership: false)]
        public void HandleClientTryChangeSRPC(byte[] packedEdits, RPCInfo info = default) {
            var edits = Packer.UnpackListFromBytes<VoxelEdit>(packedEdits);
            onVoxelsChanged?.Invoke(edits);
            HandleStateChangeORPC(packedEdits);
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

            onVoxelsChanged?.Invoke(edits);
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