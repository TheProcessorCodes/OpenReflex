using System.Linq;
namespace Reflex.Port.Content
{
    public sealed partial class ReflexLevelContent
    {
        ReflexLevelContent unleashedEnvironment;
        void ApplyUnleashedEnvironment()
        {
            var entry=UnleashedLevels.Bound;
            if(entry?.level!=level||string.IsNullOrEmpty(entry.environment_donor)||entry.environment_donor=="NAT_Concord")return;
            var donor=new ReflexLevelContent(entry.environment_donor,"FreeRide");
            donor.Open();donor.ReadScene();donor.ParseLighting();
            unleashedEnvironment=donor;lighting=donor.lighting;
            // Baked spatial lighting belongs to the donor's geometry; retain only its rig.
            colorReferenceName=null;lighting.colorReference=UnityEngine.Vector4.one;
            var skies=donor.entities.Where(e=>e.Type=="SkyRSData").ToList();
            if(skies.Count>0) { entities.RemoveAll(e=>e.Type=="SkyRSData");entities.AddRange(skies); }
        }
    }
}
