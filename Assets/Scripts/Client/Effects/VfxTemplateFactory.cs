using UnityEngine;

namespace MMO.Client
{
    /// <summary>
    /// Builds the placeholder burst the client VFX pool clones. A single small sphere is enough
    /// to prove pooling in a fight; real authored effects arrive with the art pass (#77) and
    /// replace only this factory's output.
    /// </summary>
    public static class VfxTemplateFactory
    {
        public const string TemplateName = "VfxTemplate";

        /// <summary>World size of the placeholder burst.</summary>
        public const float Scale = 0.4f;

        public static GameObject Create()
        {
            GameObject template = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            template.name = TemplateName;
            template.transform.localScale = Vector3.one * Scale;

            // The burst is a picture, not a surface: a live collider would swallow clicks aimed
            // through it in the middle of a fight.
            Collider collider = template.GetComponent<Collider>();
            if (collider != null)
                collider.enabled = false;

            Renderer renderer = template.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = new Color(1f, 0.45f, 0.08f, 1f);

            return template;
        }
    }
}
