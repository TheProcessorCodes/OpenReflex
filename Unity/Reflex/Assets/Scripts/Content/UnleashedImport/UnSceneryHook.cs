// The scenery side behind the orchestrator's seam: UnConvert.Scenery = UnSceneryHook.Run (UnConvert.cs "THE SCENERY SEAM").
// Kept apart from UnScenery.cs so the scenery side builds and is tested without the terrain side (tests/unleashed_import/scenery).
using Reflex.Port.Content.AliveImport;

namespace Reflex.Port.Content.UnleashedImport
{
    public static class UnSceneryHook
    {
        /// scenery.convert, startgate.write and stadium_lights.write for one level, as UnConvert.ConvertLevel asks for them.
        public static UnSceneryFiles Run(UnSceneryCall call)
        {
            var r = UnScenery.Convert(new UnSceneryInput
            {
                sourceDataDir = call.sourceDataDir, reflexRoot = call.reflexRoot, trackId = call.track,
                centerX = call.centerX, centerZ = call.centerZ, maxExtent = call.extent, sceneText = call.sceneText,
                terrainRxheight = call.terrainHeight, raceRxrace = call.race,
            }, call.progress, call.ct);
            return new UnSceneryFiles { files = r.files, report = (JObj)r.report, startGate = (JObj)r.startGate };
        }

        /// Sets the seam when nothing else has.
        public static void Install() { if (UnConvert.Scenery == null) UnConvert.Scenery = Run; }
    }
}
