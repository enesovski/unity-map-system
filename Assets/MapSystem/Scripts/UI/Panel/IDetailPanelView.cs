using Artifika.Map.Core;


namespace Artifika.Map.View
{
    public interface IDetailPanelView
    {
        void Show(MapMarkerContext context);
        void Hide();
    }
}