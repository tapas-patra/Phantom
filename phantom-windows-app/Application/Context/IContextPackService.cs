using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Application.Context
{
    public interface IContextPackService
    {
        System.Collections.Generic.IReadOnlyList<ContextPackSummary> GetAllPacks();
        ContextPack GetSelectedPack();
        ContextPack CreatePack(string name);
        void SelectPack(string packId);
        void SaveSelectedPack(ContextPack pack);
        void RenamePack(string packId, string name);
        void DeletePack(string packId);
        void ClearSelectedPack(bool clearResume, bool clearJobDescription);
    }
}
