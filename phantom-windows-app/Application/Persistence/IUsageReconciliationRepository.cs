using System.Collections.Generic;
using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Application.Persistence
{
    public interface IUsageReconciliationRepository
    {
        List<UsageReconciliationRecord> LoadAll();
        void SaveAll(List<UsageReconciliationRecord> records);
    }
}
