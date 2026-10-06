using MMO.Crafting;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    public sealed class CraftingPanelView : MonoBehaviour
    {
        private ICraftingSource _source;
        private CraftingStation _station;
        private readonly System.Collections.Generic.List<Button> _recipeButtons = new();

        public void Bind(ICraftingSource source)
        {
            Unbind();
            _source = source;
            if (_source == null)
                return;

            _source.RecipesChanged += Repaint;
            _source.StationOpened += OnStationOpened;
            Repaint();
        }

        public void Unbind()
        {
            if (_source != null)
            {
                _source.RecipesChanged -= Repaint;
                _source.StationOpened -= OnStationOpened;
                _source = null;
            }
        }

        private void OnStationOpened(CraftingStation station)
        {
            _station = station;
            Repaint();
        }

        private void Repaint()
        {
        }

        private void OnDestroy() => Unbind();
    }
}
