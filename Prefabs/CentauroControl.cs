using UnityEngine;

namespace ValheimMontarias.Prefabs
{
    public class CentauroControl : JavaliControl
    {
        protected override void Start()
        {
            InitPet();
            CentauroPrefab.ApplyAll(gameObject);
        }

        protected override string DashSfx => "sfx_lox_attack";
    }
}
