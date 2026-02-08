using System;
using UnityEngine;

namespace BattleCarSumo.Vehicle
{
    /// <summary>
    /// Serializable struct representing vehicle input state for network transmission.
    /// Contains throttle and steering values along with a tick for reconciliation.
    /// </summary>
    [Serializable]
    public struct VehicleInputData : IEquatable<VehicleInputData>
    {
        /// <summary>
        /// Throttle input from -1 (full reverse) to 1 (full forward).
        /// </summary>
        public float Throttle;

        /// <summary>
        /// Steering input from -1 (full left) to 1 (full right).
        /// </summary>
        public float Steering;

        /// <summary>
        /// Network tick number for reconciliation and ordering.
        /// </summary>
        public uint Tick;

        /// <summary>
        /// Initializes a new VehicleInputData struct with the provided values.
        /// </summary>
        /// <param name="throttle">Throttle input value (-1 to 1).</param>
        /// <param name="steering">Steering input value (-1 to 1).</param>
        /// <param name="tick">Network tick number.</param>
        public VehicleInputData(float throttle, float steering, uint tick)
        {
            Throttle = Mathf.Clamp(throttle, -1f, 1f);
            Steering = Mathf.Clamp(steering, -1f, 1f);
            Tick = tick;
        }

        /// <summary>
        /// Determines whether the specified VehicleInputData is equal to the current VehicleInputData.
        /// </summary>
        /// <param name="other">The VehicleInputData to compare with the current instance.</param>
        /// <returns>true if the specified VehicleInputData is equal to the current instance; otherwise, false.</returns>
        public bool Equals(VehicleInputData other)
        {
            return Mathf.Approximately(Throttle, other.Throttle) &&
                   Mathf.Approximately(Steering, other.Steering) &&
                   Tick == other.Tick;
        }

        /// <summary>
        /// Determines whether the specified object is equal to the current VehicleInputData.
        /// </summary>
        /// <param name="obj">The object to compare with the current instance.</param>
        /// <returns>true if the specified object is equal to the current instance; otherwise, false.</returns>
        public override bool Equals(object obj)
        {
            return obj is VehicleInputData other && Equals(other);
        }

        /// <summary>
        /// Serves as the default hash function.
        /// </summary>
        /// <returns>A hash code for the current instance.</returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(Throttle, Steering, Tick);
        }

        /// <summary>
        /// Returns a string representation of the VehicleInputData.
        /// </summary>
        /// <returns>A string containing the throttle, steering, and tick values.</returns>
        public override string ToString()
        {
            return $"Input(Throttle: {Throttle:F2}, Steering: {Steering:F2}, Tick: {Tick})";
        }

        /// <summary>
        /// Gets a zero input state (no throttle or steering).
        /// </summary>
        public static VehicleInputData Zero => new VehicleInputData(0f, 0f, 0);
    }
}
