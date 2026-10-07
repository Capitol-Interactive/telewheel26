// Copyright 2026 Capitol Interactive LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Collections.Generic;
using UnityEngine;

namespace Telewheel
{
    /// <summary>
    /// Confetti for the reveal and the title, and a puff of smoke for when a drawing disappears.
    /// Plain objects moved by script (no particle system), so they behave the same everywhere.
    /// </summary>
    public sealed class TwFx : MonoBehaviour
    {
        private sealed class Piece
        {
            public Transform Transform;
            public Vector3 Velocity;
            public Vector3 Spin;
            public float Life;
            public float MaxLife;
            public bool Smoke;
            public float StartScale;
        }

        private static TwFx s_Instance;
        private static Mesh s_ConfettiMesh;
        private static Mesh s_PuffMesh;

        private readonly List<Piece> m_Pieces = new List<Piece>();
        private readonly TwRandom m_Random = new TwRandom(99);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Instance = null;
            s_ConfettiMesh = null;
            s_PuffMesh = null;
        }

        public static void Init(GameObject host)
        {
            s_Instance = host.AddComponent<TwFx>();
        }

        /// <summary>A burst of candy-coloured confetti above <paramref name="center"/> (world units).</summary>
        public static void Confetti(Vector3 center, int count = 90)
        {
            if (s_Instance != null)
            {
                s_Instance.SpawnConfetti(center, count);
            }
        }

        /// <summary>A puff of pale smoke at <paramref name="center"/> (world units).</summary>
        public static void Poof(Vector3 center)
        {
            if (s_Instance != null)
            {
                s_Instance.SpawnPoof(center);
            }
        }

        private void SpawnConfetti(Vector3 center, int count)
        {
            if (s_ConfettiMesh == null)
            {
                s_ConfettiMesh = TwGfx.RoundedRect(0.28f, 0.14f, 0.04f, 2);
            }
            for (int i = 0; i < count; i++)
            {
                uint hue = TwTokens.WheelHues[m_Random.NextInt(TwTokens.WheelHues.Length)];
                GameObject go = TwGfx.Shape(transform, "Confetti", s_ConfettiMesh, hue, center);
                go.transform.rotation = Random.rotation;
                m_Pieces.Add(new Piece
                {
                    Transform = go.transform,
                    Velocity = new Vector3(
                        m_Random.NextRange(-9f, 9f), m_Random.NextRange(6f, 16f), m_Random.NextRange(-9f, 9f)),
                    Spin = new Vector3(
                        m_Random.NextRange(-400f, 400f), m_Random.NextRange(-400f, 400f), m_Random.NextRange(-400f, 400f)),
                    Life = m_Random.NextRange(1.6f, 2.6f),
                    MaxLife = 2.6f,
                });
            }
        }

        private void SpawnPoof(Vector3 center)
        {
            if (s_PuffMesh == null)
            {
                s_PuffMesh = TwGfx.Disc(1f, 16);
            }
            for (int i = 0; i < 14; i++)
            {
                uint shade = i % 2 == 0 ? TwTokens.InkMuted : TwTokens.Ink;
                GameObject go = TwGfx.Shape(transform, "Puff", s_PuffMesh, shade, center);
                float size = m_Random.NextRange(1.2f, 2.6f);
                go.transform.localScale = Vector3.one * size;
                Vector3 direction = new Vector3(
                    m_Random.NextRange(-1f, 1f), m_Random.NextRange(-0.2f, 1f), m_Random.NextRange(-1f, 1f));
                m_Pieces.Add(new Piece
                {
                    Transform = go.transform,
                    Velocity = direction.normalized * m_Random.NextRange(4f, 11f),
                    Life = m_Random.NextRange(0.5f, 0.8f),
                    MaxLife = 0.8f,
                    Smoke = true,
                    StartScale = size,
                });
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = m_Pieces.Count - 1; i >= 0; i--)
            {
                Piece piece = m_Pieces[i];
                piece.Life -= dt;
                if (piece.Life <= 0f || piece.Transform == null)
                {
                    if (piece.Transform != null)
                    {
                        Destroy(piece.Transform.gameObject);
                    }
                    m_Pieces.RemoveAt(i);
                    continue;
                }
                if (piece.Smoke)
                {
                    // Smoke drifts out and shrinks away, since flat colours cannot fade.
                    piece.Velocity *= Mathf.Exp(-3f * dt);
                    float k = piece.Life / piece.MaxLife;
                    piece.Transform.localScale = Vector3.one * (piece.StartScale * k);
                    FaceViewer(piece.Transform);
                }
                else
                {
                    piece.Velocity += Vector3.down * 18f * dt;
                    piece.Transform.Rotate(piece.Spin * dt, Space.Self);
                }
                piece.Transform.position += piece.Velocity * dt;
            }
        }

        private static void FaceViewer(Transform t)
        {
            Transform head = OpenBrushFacade.Head;
            if (head != null)
            {
                t.rotation = Quaternion.LookRotation(t.position - head.position, Vector3.up);
            }
        }
    }
}
