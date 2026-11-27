// MATERIALS: ENHANCED (OPTIONS > VIDEO; Shaders/ReflexVehiclePBR.hlsl): which physically based class a vehicle / rider surface is. Not part of the original
// game: the game's materials only name a shader asset (the program: Template_Machines_Frame_FNL, Jersey_FNL, ...), a material (MX_Plstc, MX_Frame_Alu,
// Boots_Answer_White ...) and textures (diffuse, combo / SFX map, normal), so the class is read from those names. Reflex/Vehicle's _RfxMat.x holds it
// (ReflexObjectShading.Build sets it on every material it makes); 0 leaves the original program's picture as it is.
//   1 PAINT PLASTIC  graphic-kit plastics, fenders, tanks, body panels, chest protector shells: clear coat over a rough base
//   2 MATTE PLASTIC  hand guards, nerf bars, bumpers, neck brace, goggle frame, matte helmets
//   3 FRAME          painted / powder-coated frame, swingarm, A-arms, tubes: part metal under a clear coat
//   4 CHROME         chromed exhausts and fasteners
//   5 ANODISED       handlebars, hubs, clamps, rims' anodised parts: coloured metal
//   6 BRUSHED METAL  engine cases, muffler, flywheels
//   7 RUBBER TYRE    tyres, belts
//   8 SEAT / GRIP    seat cover, grips, pads
//   9 FABRIC         jersey, pants, gloves, helmet liner, goggle strap, nylon: sheen and weave
//  10 LEATHER / BOOT boots
//  11 HELMET SHELL   glossy helmet paint
//  12 LENS           goggle lens
//  13 SKIN           the rider's skin, head
//  14 BODY PAINT     the machines' (trucks, buggies, UTVs) painted bodies: metallic clear coat
//  15 WHEEL RIM      wheels and hubs
//  16 SPRING / SHOCK coil springs, shock bodies, fork metal
// The material's own maps (_RfxMatMap; Shaders/ReflexVehiclePBR.hlsl "the material's maps"): which of its texture slots holds the diffuse map (every
// material: by the sampler's / the texture's name) and, for the programs whose slots were read from their code (Maps below), the SFX / combo map and the
// channel of it that is the surface's shine, so the roughness and the specular follow the map and a metal's F0 comes from its own colour.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Reflex.Port.Content
{
    public static class ReflexMaterialClasses
    {
        public enum Kind
        {
            None = 0, PaintPlastic = 1, MattePlastic = 2, Frame = 3, Chrome = 4, Anodised = 5, BrushedMetal = 6, Tire = 7, SeatGrip = 8, Fabric = 9,
            Leather = 10, Helmet = 11, Lens = 12, Skin = 13, BodyPaint = 14, Rim = 15, Spring = 16
        }

        static readonly int MatId = Shader.PropertyToID("_RfxMat"), MapId = Shader.PropertyToID("_RfxMatMap");
        static readonly int[] TexIds = { Shader.PropertyToID("_Tex0"), Shader.PropertyToID("_Tex1"), Shader.PropertyToID("_Tex2"), Shader.PropertyToID("_Tex3"),
                                         Shader.PropertyToID("_Tex4"), Shader.PropertyToID("_Tex5"), Shader.PropertyToID("_Tex6") };
        /// The slot only ENHANCED reads a normal map from (a .rxmod material's, on a program without one: ReflexBikeModel.RxMod.cs); no program of
        /// the .rxmod templates samples it.
        public const int EnhancedNormalSlot = 6;

        /// The programs whose SFX / combo map is read for the shine (the program's code says what its channels are: ReflexObjectPrograms.hlsl), by a part
        /// of the pixel shader's name: the channel (0 red: the frame's / engine's shine blend, the wheel's specular; 2 blue: the plastics' reflection).
        static readonly (string shader, int channel)[] Maps =
        {
            ("template_vehicle_plstc_fnl", 2), ("template_atv_plastic_fnl", 2), ("template_mx_frame_fnl", 0), ("template_mx_frame_ui_fnl", 0),
            ("template_atv_mx_engine_fnl", 0), ("template_mx_wheel_fnl", 0),
        };
        static readonly string[] NotDiffuse = { "SFX", "Numbers", "Mud_Noise", "NRML", "Norm", "Combo", "Fork", "namnum", "dirtmask", "Mud_WET", "ReflectionMap", "Cube" };

        static bool IsSfx(string n) => n != null && (n.EndsWith("_SFX", StringComparison.OrdinalIgnoreCase) || n.IndexOf("Combo", StringComparison.OrdinalIgnoreCase) >= 0);

        /// The texture slot of a game material's diffuse map (-1: none found): the sampler the program calls its diffuse, else the first texture
        /// whose name is none of the other maps'.
        internal static int DiffuseSlot(VehicleMaterialDef d)
        {
            if (d == null) return -1;
            for (int i = 0; i < d.textures.Count; i++) if (d.textures[i].sampler != null && d.textures[i].sampler.IndexOf("diffuse", StringComparison.OrdinalIgnoreCase) >= 0) return i;
            for (int i = 0; i < d.textures.Count; i++)
            {
                string n = d.textures[i].name; if (string.IsNullOrEmpty(n)) continue;
                bool other = false; foreach (string x in NotDiffuse) if (n.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0) { other = true; break; }
                if (!other) return i;
            }
            return -1;
        }

        /// The diffuse map of a Reflex/Vehicle material (the slot Apply found; null: another material, or none).
        public static Texture DiffuseTexture(Material m)
        {
            if (!m || !m.HasProperty(MapId)) return null;
            int slot = Mathf.RoundToInt(m.GetVector(MapId).w) - 1;
            return slot >= 0 && slot < TexIds.Length && m.HasProperty(TexIds[slot]) ? m.GetTexture(TexIds[slot]) : null;
        }

        /// A normal map only ENHANCED reads (the material's program has none): its shading normal for the specular, the clear coat and the rain film.
        public static void SetEnhancedNormal(Material m, Texture normal)
        {
            if (!m || !normal || !m.HasProperty(MapId)) return;
            m.SetTexture(TexIds[EnhancedNormalSlot], normal);
            var v = m.GetVector(MapId); v.z = EnhancedNormalSlot + 1; m.SetVector(MapId, v);
        }

        static readonly bool Log = Environment.GetEnvironmentVariable("REFLEX_MATLOG") == "1";

        /// The class of a material: shader = its pixel shader asset's name, material = the material's name, textures = its texture names, label = what the
        /// builder called it (the binding, the gear part).
        public static Kind Classify(string shader, string material, IEnumerable<string> textures, string label)
        {
            string sh = (shader ?? "").ToLowerInvariant(), mat = (material ?? "").ToLowerInvariant(), lab = (label ?? "").ToLowerInvariant();
            string tex = textures == null ? "" : string.Join(" ", textures).ToLowerInvariant();
            string all = sh + " " + mat + " " + lab + " " + tex;
            bool Has(string s, params string[] any) { foreach (var a in any) if (s.Contains(a)) return true; return false; }

            // crowds, characters, plain colours: the original's
            if (Has(sh, "crowd", "editor_", "hair", "teeth", "earring", "eye")) return Kind.None;
            if (sh.StartsWith("pro40", StringComparison.Ordinal) || sh == "template_diffuse" || sh == "template_machines_difspecalpha") return Kind.None;

            // rider gear and body
            if (Has(sh, "lens_")) return Kind.Lens;
            if (Has(sh, "helmet_cloth", "helmet_gstrap", "gstrap")) return Kind.Fabric;
            if (Has(sh, "helmet_matte")) return Kind.MattePlastic;
            if (Has(sh, "helmet_")) return Kind.Helmet;
            if (Has(sh, "goggle_")) return Kind.MattePlastic;
            if (Has(sh, "boots_", "body_boots")) return Kind.Leather;
            if (Has(sh, "chestprt_cloth")) return Kind.Fabric;
            if (Has(sh, "chestprt_")) return Kind.PaintPlastic;
            if (Has(sh, "nckbrace")) return Kind.MattePlastic;
            if (Has(sh, "jersey", "body_pants", "skin_nylon")) return Kind.Fabric;
            if (Has(sh, "body_skin", "body_head", "skin_fnl")) return Kind.Skin;
            if (Has(sh, "body_")) return Kind.Fabric;

            // vehicles
            if (Has(sh, "tire", "belts")) return Kind.Tire;
            if (Has(sh, "exhst_chr")) return Kind.Chrome;
            if (Has(sh, "exhst_mtl", "flywheel")) return Kind.BrushedMetal;
            if (Has(sh, "hndlebrs_spec")) return Kind.Anodised;
            if (Has(sh, "hndlebrs_brshmtl")) return Kind.BrushedMetal;
            if (Has(sh, "hbars_pad", "handgrd", "nerf")) return Kind.MattePlastic;
            if (Has(sh, "hbars")) return Kind.Anodised;
            if (Has(sh, "mx_hub")) return Kind.Rim;
            if (Has(sh, "wheel")) return Kind.Rim;
            if (Has(sh, "spring", "shock_metal", "shock_body")) return Kind.Spring;
            if (Has(sh, "engine")) return Kind.BrushedMetal;
            if (Has(sh, "seat")) return Kind.SeatGrip;
            if (Has(sh, "machinebody", "machines_body")) return Kind.BodyPaint;
            // the alpha-tested metal (Template_ATV_MX_AlphaMtl: the chain, sprocket teeth, guards' mesh) and anything named a chain: bare steel, not
            // the frame's painted metal under a clear coat (a chain drew with a lacquer's sharp highlight)
            if (Has(sh, "alphamtl") || Has(mat, "chain")) return Kind.BrushedMetal;
            if (Has(sh, "bumper", "aarms", "frame", "tube")) return Kind.Frame;
            if (Has(sh, "plstc", "plastic", "plst")) return Kind.PaintPlastic;
            return Kind.None;
        }

        /// Classifies a material by its game definition and writes the class on the Reflex/Vehicle material (called by ReflexObjectShading.Build).
        internal static Kind Apply(Material m, string programName, VehicleMaterialDef d, string label)
        {
            var names = new List<string>();
            if (d != null) foreach (var t in d.textures) if (!string.IsNullOrEmpty(t.name)) names.Add(t.name);
            var kind = Classify(d?.pixelShader ?? programName, d?.name, names, label);
            // the material's own maps: the diffuse slot (every material), the SFX map's shine and the diffuse as the albedo where the program is known
            int diffuse = DiffuseSlot(d), sfx = -1, channel = 0; bool known = false;
            string sh = (d?.pixelShader ?? programName ?? "").ToLowerInvariant();
            foreach (var (shader, ch) in Maps)
            {
                if (!sh.StartsWith(shader, StringComparison.Ordinal)) continue;
                known = true; channel = ch;
                for (int i = 0; d != null && i < d.textures.Count && i < 4; i++) if (IsSfx(d.textures[i].name)) { sfx = i; break; }
                break;
            }
            m.SetVector(MatId, new Vector4((float)kind, 0, known && diffuse >= 0 && diffuse < 4 ? 1 : 0, 0));
            m.SetVector(MapId, new Vector4(sfx + 1, channel, 0, diffuse + 1));
            if (Log) Debug.Log($"[matclass] {kind} (sfx slot {sfx} channel {channel}, diffuse slot {diffuse}{(known ? "" : ", maps not read")}) <- shader {d?.pixelShader ?? programName} material {d?.name} label {label} textures [{string.Join(", ", names)}]");
            return kind;
        }
    }
}
