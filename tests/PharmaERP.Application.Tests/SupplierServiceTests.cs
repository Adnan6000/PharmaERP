using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Services;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.Tests;

public class SupplierServiceTests
{
    private class FakeSupplierRepository : ISupplierRepository
    {
        public readonly List<Supplier> Suppliers = [];

        public Task<PagedResult<SupplierDto>> GetPagedAsync(PaginationQuery query, string? searchTerm = null, CancellationToken cancellationToken = default)
        {
            var filtered = Suppliers.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                filtered = filtered.Where(s => s.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                                               s.SupplierCode.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
            }

            int total = filtered.Count();
            var items = filtered.Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .Select(s => new SupplierDto(s.Id, s.SupplierCode, s.Name, s.ContactPerson, s.Phone, s.Address, s.IsActive, s.RowVersion))
                .ToList();

            return Task.FromResult(new PagedResult<SupplierDto>(items, total, query.PageNumber, query.PageSize));
        }

        public Task<Supplier?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Suppliers.FirstOrDefault(s => s.Id == id));

        public Task<Supplier> AddAsync(Supplier supplier, CancellationToken cancellationToken = default)
        {
            supplier.Id = Suppliers.Count + 1;
            Suppliers.Add(supplier);
            return Task.FromResult(supplier);
        }

        public Task UpdateAsync(Supplier supplier, CancellationToken cancellationToken = default)
        {
            var idx = Suppliers.FindIndex(s => s.Id == supplier.Id);
            if (idx >= 0) Suppliers[idx] = supplier;
            return Task.CompletedTask;
        }

        public Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default)
        {
            var s = Suppliers.FirstOrDefault(x => x.Id == id);
            if (s != null)
            {
                s.IsActive = !s.IsActive;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<bool> ExistsCodeAsync(string code, int? excludeId = null, CancellationToken cancellationToken = default)
        {
            bool exists = Suppliers.Any(s => s.SupplierCode.Equals(code, StringComparison.OrdinalIgnoreCase) && (!excludeId.HasValue || s.Id != excludeId.Value));
            return Task.FromResult(exists);
        }

        public Task<IReadOnlyList<LookupDto>> GetActiveLookupAsync(CancellationToken cancellationToken = default)
        {
            var lookups = Suppliers.Where(s => s.IsActive).Select(s => new LookupDto(s.Id, s.Name, s.SupplierCode)).ToList();
            return Task.FromResult<IReadOnlyList<LookupDto>>(lookups);
        }
    }

    private class FakeDocNumberGen : IDocumentNumberGenerator
    {
        private int _counter = 0;
        public Task<string> NextDocumentNumberAsync(DocumentType type, int? year = null, CancellationToken cancellationToken = default)
        {
            _counter++;
            return Task.FromResult($"SUPP-{_counter:D5}");
        }
    }

    [Fact]
    public async Task CreateSupplierAsync_GeneratesCode_WhenBlank()
    {
        var repo = new FakeSupplierRepository();
        var docGen = new FakeDocNumberGen();
        var service = new SupplierService(repo, docGen);

        var result = await service.CreateSupplierAsync(new SupplierUpsertDto
        {
            Name = "Apex Pharma Distributors"
        });

        Assert.Equal("SUPP-00001", result.SupplierCode);
        Assert.Equal("Apex Pharma Distributors", result.Name);
        Assert.Single(repo.Suppliers);
    }

    [Fact]
    public async Task CreateSupplierAsync_ThrowsDuplicate_WhenCodeExists()
    {
        var repo = new FakeSupplierRepository();
        repo.Suppliers.Add(new Supplier { Id = 1, SupplierCode = "SUPP-001", Name = "Existing Vendor" });
        var service = new SupplierService(repo, new FakeDocNumberGen());

        await Assert.ThrowsAsync<DuplicateKeyException>(() => service.CreateSupplierAsync(new SupplierUpsertDto
        {
            SupplierCode = "SUPP-001",
            Name = "Another Vendor"
        }));
    }
}

