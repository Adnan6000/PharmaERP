using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Services;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.Tests;

public class MasterServicesTests
{
    private class FakeManufacturerRepository : IManufacturerRepository
    {
        public readonly List<Manufacturer> Items = [];

        public Task<IReadOnlyList<ManufacturerDto>> GetAllAsync(string? searchTerm = null, CancellationToken cancellationToken = default)
        {
            var query = Items.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                query = query.Where(m => m.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
            }

            var list = query
                .Select(m => new ManufacturerDto(m.Id, m.Name, m.ContactPerson, m.Phone, m.Email, m.IsActive, m.RowVersion))
                .ToList();

            return Task.FromResult<IReadOnlyList<ManufacturerDto>>(list);
        }

        public Task<Manufacturer?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(m => m.Id == id));

        public Task<Manufacturer> AddAsync(Manufacturer entity, CancellationToken cancellationToken = default)
        {
            entity.Id = Items.Count + 1;
            Items.Add(entity);
            return Task.FromResult(entity);
        }

        public Task UpdateAsync(Manufacturer entity, CancellationToken cancellationToken = default)
        {
            var idx = Items.FindIndex(m => m.Id == entity.Id);
            if (idx >= 0) Items[idx] = entity;
            return Task.CompletedTask;
        }

        public Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default)
        {
            var item = Items.FirstOrDefault(m => m.Id == id);
            if (item != null)
            {
                item.IsActive = !item.IsActive;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<bool> ExistsNameAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.Any(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && (!excludeId.HasValue || m.Id != excludeId.Value)));
        }

        public Task<IReadOnlyList<LookupDto>> GetActiveLookupAsync(CancellationToken cancellationToken = default)
        {
            var list = Items.Where(m => m.IsActive).Select(m => new LookupDto(m.Id, m.Name)).ToList();
            return Task.FromResult<IReadOnlyList<LookupDto>>(list);
        }
    }

    private class FakeCategoryRepository : ICategoryRepository
    {
        public readonly List<Category> Items = [];

        public Task<IReadOnlyList<CategoryDto>> GetAllAsync(string? searchTerm = null, CancellationToken cancellationToken = default)
        {
            var query = Items.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                query = query.Where(c => c.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
            }

            var list = query
                .Select(c => new CategoryDto(c.Id, c.Name, c.Description, c.IsActive, c.RowVersion))
                .ToList();

            return Task.FromResult<IReadOnlyList<CategoryDto>>(list);
        }

        public Task<Category?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(c => c.Id == id));

        public Task<Category> AddAsync(Category entity, CancellationToken cancellationToken = default)
        {
            entity.Id = Items.Count + 1;
            Items.Add(entity);
            return Task.FromResult(entity);
        }

        public Task UpdateAsync(Category entity, CancellationToken cancellationToken = default)
        {
            var idx = Items.FindIndex(c => c.Id == entity.Id);
            if (idx >= 0) Items[idx] = entity;
            return Task.CompletedTask;
        }

        public Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default)
        {
            var item = Items.FirstOrDefault(c => c.Id == id);
            if (item != null)
            {
                item.IsActive = !item.IsActive;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<bool> ExistsNameAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && (!excludeId.HasValue || c.Id != excludeId.Value)));
        }

        public Task<IReadOnlyList<LookupDto>> GetActiveLookupAsync(CancellationToken cancellationToken = default)
        {
            var list = Items.Where(c => c.IsActive).Select(c => new LookupDto(c.Id, c.Name)).ToList();
            return Task.FromResult<IReadOnlyList<LookupDto>>(list);
        }
    }

    private class FakeUnitRepository : IUnitRepository
    {
        public readonly List<Unit> Items = [];

        public Task<IReadOnlyList<UnitDto>> GetAllAsync(string? searchTerm = null, CancellationToken cancellationToken = default)
        {
            var query = Items.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                query = query.Where(u => u.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) || u.Abbreviation.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
            }

            var list = query
                .Select(u => new UnitDto(u.Id, u.Name, u.Abbreviation, u.IsActive, u.RowVersion))
                .ToList();

            return Task.FromResult<IReadOnlyList<UnitDto>>(list);
        }

        public Task<Unit?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(u => u.Id == id));

        public Task<Unit> AddAsync(Unit entity, CancellationToken cancellationToken = default)
        {
            entity.Id = Items.Count + 1;
            Items.Add(entity);
            return Task.FromResult(entity);
        }

        public Task UpdateAsync(Unit entity, CancellationToken cancellationToken = default)
        {
            var idx = Items.FindIndex(u => u.Id == entity.Id);
            if (idx >= 0) Items[idx] = entity;
            return Task.CompletedTask;
        }

        public Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default)
        {
            var item = Items.FirstOrDefault(u => u.Id == id);
            if (item != null)
            {
                item.IsActive = !item.IsActive;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<bool> ExistsNameAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.Any(u => u.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && (!excludeId.HasValue || u.Id != excludeId.Value)));
        }

        public Task<bool> ExistsAbbreviationAsync(string abbreviation, int? excludeId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.Any(u => u.Abbreviation.Equals(abbreviation, StringComparison.OrdinalIgnoreCase) && (!excludeId.HasValue || u.Id != excludeId.Value)));
        }

        public Task<IReadOnlyList<LookupDto>> GetActiveLookupAsync(CancellationToken cancellationToken = default)
        {
            var list = Items.Where(u => u.IsActive).Select(u => new LookupDto(u.Id, $"{u.Name} ({u.Abbreviation})")).ToList();
            return Task.FromResult<IReadOnlyList<LookupDto>>(list);
        }
    }

    [Fact]
    public async Task ManufacturerService_CreateAsync_ThrowsWhenDuplicateName()
    {
        var repo = new FakeManufacturerRepository();
        repo.Items.Add(new Manufacturer { Id = 1, Name = "GSK" });
        var service = new ManufacturerService(repo);

        var dto = new ManufacturerUpsertDto { Name = "GSK" };

        await Assert.ThrowsAsync<DuplicateKeyException>(() => service.CreateAsync(dto));
    }

    [Fact]
    public async Task ManufacturerService_CreateAsync_SucceedsWithValidData()
    {
        var repo = new FakeManufacturerRepository();
        var service = new ManufacturerService(repo);

        var dto = new ManufacturerUpsertDto { Name = "Pfizer", Email = "contact@pfizer.com" };
        var created = await service.CreateAsync(dto);

        Assert.Equal(1, created.Id);
        Assert.Equal("Pfizer", created.Name);
        Assert.Equal("contact@pfizer.com", created.Email);
    }

    [Fact]
    public async Task CategoryService_CreateAsync_ThrowsWhenDuplicateName()
    {
        var repo = new FakeCategoryRepository();
        repo.Items.Add(new Category { Id = 1, Name = "Antibiotics" });
        var service = new CategoryService(repo);

        var dto = new CategoryUpsertDto { Name = "Antibiotics" };

        await Assert.ThrowsAsync<DuplicateKeyException>(() => service.CreateAsync(dto));
    }

    [Fact]
    public async Task CategoryService_CreateAsync_SucceedsWithValidData()
    {
        var repo = new FakeCategoryRepository();
        var service = new CategoryService(repo);

        var dto = new CategoryUpsertDto { Name = "Analgesics", Description = "Pain relief" };
        var created = await service.CreateAsync(dto);

        Assert.Equal(1, created.Id);
        Assert.Equal("Analgesics", created.Name);
    }

    [Fact]
    public async Task UnitService_CreateAsync_ThrowsWhenDuplicateAbbreviation()
    {
        var repo = new FakeUnitRepository();
        repo.Items.Add(new Unit { Id = 1, Name = "Tablet", Abbreviation = "TAB" });
        var service = new UnitService(repo);

        var dto = new UnitUpsertDto { Name = "Tabular", Abbreviation = "TAB" };

        await Assert.ThrowsAsync<DuplicateKeyException>(() => service.CreateAsync(dto));
    }

    [Fact]
    public async Task UnitService_CreateAsync_SucceedsWithValidData()
    {
        var repo = new FakeUnitRepository();
        var service = new UnitService(repo);

        var dto = new UnitUpsertDto { Name = "Capsule", Abbreviation = "CAP" };
        var created = await service.CreateAsync(dto);

        Assert.Equal(1, created.Id);
        Assert.Equal("Capsule", created.Name);
        Assert.Equal("CAP", created.Abbreviation);
    }
}
