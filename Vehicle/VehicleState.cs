using System;
using UnityEngine;

namespace BattleCarSumo.Vehicle
{
    /// <summary>
    /// Represents the complete physics state of a vehicle at a specific network tick.
    /// Used for synchronization and reconciliation between client and server.
    /// </summary>
    [Serializable]
    public struct VehicleState : IEquatable<VehicleState>
    {
        /// <summary>
        /// World position of the vehicle.
        /// </summary>
        public Vector3 Position;

        /// <summary>
        /// World rotation of the vehicle.
        /// </summary>
        public Quaternion Rotation;

        /// <summary>
        /// Linear velocity of the vehicle.
        /// </summary>
        public Vector3 Velocity;

        /// <summary>
        /// Angular velocity of the vehicle.
        /// </summary>
        public Vector3 AngularVelocity;

        /// <summary>
        /// Network tick at which this state was recorded.
        /// </summary>
        public uint Tick;

        /// <summary>
        /// Initializes a new VehicleState with the provided values.
        /// </summary>
        public VehicleState(Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity, uint tick)
        {
            Position = position;
            Rotation = rotation;
            Velocity = velocity;
            AngularVelocity = angularVelocity;
            Tick = tick;
        }

        /// <summary>
        /// Determines whether the specified VehicleState is equal to the current VehicleState.
        /// </summary>
        public bool Equals(VehicleState other)
        {
            return Position == other.Position &&
                   Rotation == other.Rotation &&
                   Velocity == other.Velocity &&
                   AngularVelocity == other.AngularVelocity &&
                   Tick == other.Tick;
        }

        /// <summary>
        /// Determines whether the specified object is equal to the current VehicleState.
        /// </summary>
        public override bool Equals(object obj)
        {
            return obj is VehicleState other && Equals(other);
        }

        /// <summary>
        /// Serves as the default hash function.
        /// </summary>
        public override int GetHashCode()
        {
            return HashCode.Combine(Position, Rotation, Velocity, AngularVelocity, Tick);
        }

        /// <summary>
        /// Returns a string representation of the VehicleState.
        /// </summary>
        public override string ToString()
        {
            return $"VehicleState(Pos: {Position}, Rot: {Rotation.eulerAngles}, Vel: {Velocity}, Tick: {Tick})";
        }

        /// <summary>
        /// Performs linear interpolation between two VehicleStates.
        /// </summary>
        /// <param name="from">The starting state.</param>
        /// <param name="to">The ending state.</param>
        /// <param name="t">Interpolation factor (0 to 1).</param>
        /// <returns>An interpolated VehicleState.</returns>
        public static VehicleState Lerp(VehicleState from, VehicleState to, float t)
        {
            t = Mathf.Clamp01(t);
            return new VehicleState
            {
                Position = Vector3.Lerp(from.Position, to.Position, t),
                Rotation = Quaternion.Lerp(from.Rotation, to.Rotation, t),
                Velocity = Vector3.Lerp(from.Velocity, to.Velocity, t),
                AngularVelocity = Vector3.Lerp(from.AngularVelocity, to.AngularVelocity, t),
                Tick = from.Tick
            };
        }
    }
}
