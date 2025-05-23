// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace UnityEngine
{
    // Representation of rays.
    public partial struct Ray : IFormattable
    {
        private Vector3 m_Origin;
        private Vector3 m_Direction;

        // Creates a ray starting at /origin/ along /direction/.
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public Ray(Vector3 origin, Vector3 direction)
        {
            m_Origin = origin;
            m_Direction = direction.normalized;
        }

        // The origin point of the ray.
        public Vector3 origin
        {
            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)] get { return m_Origin; }
            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)] set { m_Origin = value; }
        }

        // The direction of the ray.
        public Vector3 direction
        {
            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)] get { return m_Direction; }
            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)] set { m_Direction = value.normalized; }
        }

        // Returns a point at /distance/ units along the ray.
        public Vector3 GetPoint(float distance)
        {
            return m_Origin + m_Direction * distance;
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public override string ToString()
        {
            return ToString(null, null);
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public string ToString(string format)
        {
            return ToString(format, null);
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (string.IsNullOrEmpty(format))
                format = "F2";
            if (formatProvider == null)
                formatProvider = CultureInfo.InvariantCulture.NumberFormat;
            return string.Format("Origin: {0}, Dir: {1}", m_Origin.ToString(format, formatProvider), m_Direction.ToString(format, formatProvider));
        }
    }
}
