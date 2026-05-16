// Ported from CodeWalker's VertexTypes.cs — dexyfex/CodeWalker (MIT License)
// Builds a DirectX InputElement[] from GTA V's VertexType flags + VertexDeclarationTypes.
// 
// KEY DESIGN: Our simple VS only reads POSITION, NORMAL, TEXCOORD0.
// We scan the full vertex declaration to find the exact byte offset of each needed
// component, then build InputElements pointing directly at those offsets.
// All other components are silently skipped — their bytes are still in the vertex
// buffer but D3D11 ignores them since the InputLayout doesn't reference them.
using CodeWalker.GameFiles;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System.Collections.Generic;

namespace ToolKitV.Rendering
{
    public static class GtaVertexLayout
    {
        // GTA V semantic slot → HLSL semantic name (16 slots, matches CodeWalker exactly)
        public static readonly string[] Semantics = new string[16]
        {
            "POSITION",      // 0
            "BLENDWEIGHTS",  // 1
            "BLENDINDICES",  // 2
            "NORMAL",        // 3
            "COLOR",         // 4  (COLOR0)
            "COLOR",         // 5  (COLOR1)
            "TEXCOORD",      // 6  (TEXCOORD0)
            "TEXCOORD",      // 7  (TEXCOORD1)
            "TEXCOORD",      // 8  (TEXCOORD2)
            "TEXCOORD",      // 9  (TEXCOORD3)
            "TEXCOORD",      // 10 (TEXCOORD4)
            "TEXCOORD",      // 11 (TEXCOORD5)
            "TEXCOORD",      // 12 (TEXCOORD6)
            "TEXCOORD",      // 13 (TEXCOORD7)
            "TANGENT",       // 14
            "BINORMAL",      // 15
        };

        public static Format GetDxgiFormat(VertexComponentType type)
        {
            return type switch
            {
                VertexComponentType.Half2      => Format.R16G16_Float,
                VertexComponentType.Float      => Format.R32_Float,
                VertexComponentType.Half4      => Format.R16G16B16A16_Float,
                VertexComponentType.Float2     => Format.R32G32_Float,
                VertexComponentType.Float3     => Format.R32G32B32_Float,
                VertexComponentType.Float4     => Format.R32G32B32A32_Float,
                VertexComponentType.UByte4     => Format.R8G8B8A8_UInt,
                VertexComponentType.Colour     => Format.R8G8B8A8_UNorm,
                VertexComponentType.RGBA8SNorm => Format.R8G8B8A8_SNorm,
                _                              => Format.Unknown,
            };
        }

        /// <summary>
        /// Builds InputElements for ONLY the semantics our VS shader uses:
        ///   POSITION0, NORMAL0, TEXCOORD0
        ///
        /// Scans the full vertex declaration to find the exact byte offset of each
        /// component in the packed vertex buffer — even when surrounded by BlendWeights,
        /// Color, Tangent etc. that our shader doesn't need.
        ///
        /// Returns null if POSITION is not found (geometry can't be rendered).
        /// </summary>
        public static InputElement[]? GetLayoutForSimpleShader(
            VertexType componentsFlags,
            VertexDeclarationTypes componentsTypes)
        {
            var types  = (ulong)componentsTypes;
            var flags  = (uint)componentsFlags;

            int offset = 0;
            // Track per-semantic-NAME index (not per slot) so TEXCOORD at slots 6,7,8 get indices 0,1,2
            var semanticCounts = new Dictionary<string, int>();
            var result = new List<InputElement>(3);

            for (int k = 0; k < 16; k++)
            {
                if (((flags >> k) & 0x1) == 0) continue;

                var compType = (VertexComponentType)((types >> (k * 4)) & 0x0F);
                if (compType == VertexComponentType.Nothing) continue;

                int size = VertexComponentTypes.GetSizeInBytes(compType);
                if (size == 0) continue;

                var semName = Semantics[k];
                semanticCounts.TryGetValue(semName, out int semIdx);
                semanticCounts[semName] = semIdx + 1;

                // Emit only POSITION/0, NORMAL/0, TEXCOORD/0 — matching VS_IN exactly
                bool want = (semName == "POSITION" && semIdx == 0)
                         || (semName == "NORMAL"   && semIdx == 0)
                         || (semName == "TEXCOORD" && semIdx == 0);

                if (want)
                {
                    var fmt = GetDxgiFormat(compType);
                    if (fmt != Format.Unknown)
                        result.Add(new InputElement(semName, semIdx, fmt, offset, 0));
                }

                offset += size;
            }

            return result.Count > 0 ? result.ToArray() : null;
        }
    }
}

