using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Services;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.Tests;

public class CustomerServiceTests
{
    private class FakeCustomerRepository : ICustomerRepository
    {
        public readonly List<Customer> Customers = [];

        public Task<PagedResult<CustomerDto>> GetPagedAsync(PaginationQuery query, string? searchTerm = null, CancellationToken cancellationToken = default)
        {
            var filtered = Customers.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                filtered = filtered.Where(c => c.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                                               c.CustomerCode.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
            }

            int total = filtered.Count();
            var items = filtered.Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .Select(c => new CustomerDto(c.Id, c.CustomerCode, c.Name, c.ContactPerson, c.Phone, c.Address, c.CreditLimit, c.IsActive, c.RowVersion))
                .ToList();

            return Task.FromResult(new PagedResult<CustomerDto>(items, total, query.PageNumber, query.PageSize));
        }

        public Task<Customer?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Customers.FirstOrDefault(c => c.Id == id));

        public Task<Customer> AddAsync(Customer customer, CancellationToken cancellationToken = default)
        {
            customer.Id = Customers.Count + 1;
            Customers.Add(customer);
            return Task.FromResult(customer);
        }

        public Task UpdateAsync(Customer customer, CancellationToken cancellationToken = default)
        {
            var idx = Customers.FindIndex(c => c.Id == customer.Id);
            if (idx >= 0) Customers[idx] = customer;
            return Task.CompletedTask;
        }

        public Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default)
        {
            var c = Customers.FirstOrDefault(x => x.Id == id);
            if (c != null)
            {
                c.IsActive = !c.IsActive;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<bool> ExistsCodeAsync(string code, int? excludeId = null, CancellationToken cancellationToken = default)
        {
            bool exists = Customers.Any(c => c.CustomerCode.Equals(code, StringComparison.OrdinalIgnoreCase) && (!excludeId.HasValue || c.Id != excludeId.Value));
            return Task.FromResult(exists);
        }

        public Task<IReadOnlyList<LookupDto>> GetActiveLookupAsync(CancellationToken cancellationToken = default)
        {
            var lookups = Customers.Where(c => c.IsActive).Select(c => new LookupDto(c.Id, c.Name, c.CustomerCode)).ToList();
            return Task.FromResult<IReadOnlyList<LookupDto>>(lookups);
        }
    }

    private class FakeDocNumberGen : IDocumentNumberGenerator
    {
        private int _counter = 0;
        public Task<string> NextDocumentNumberAsync(DocumentType type, int? year = null, CancellationToken cancellationToken = default)
        {
            _counter++;
            return Task.FromResult($"CUST-{_counter:D5}");
        }
    }

    [Fact]
    public async Task CreateCustomerAsync_GeneratesCode_WhenBlank()
    {
        var repo = new FakeCustomerRepository();
        var docGen = new FakeDocNumberGen();
        var service = new CustomerService(repo, docGen);

        var result = await service.CreateCustomerAsync(new CustomerUpsertDto
        {
            Name = "City Clinic",
            CreditLimit = 50000m
        });

        Assert.Equal("CUST-00001", result.CustomerCode);
        Assert.Equal("City Clinic", result.Name);
        Assert.Equal(50000m, result.CreditLimit);
        Assert.Single(repo.Customers);
    }

    [Fact]
    public async Task CreateCustomerAsync_ThrowsValidation_WhenNameBlank()
    {
        var repo = new FakeCustomerRepository();
        var service = new CustomerService(repo, new FakeDocNumberGen());

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateCustomerAsync(new CustomerUpsertDto
        {
            Name = "  "
        }));
    }

    [Fact]
    public async Task CreateCustomerAsync_ThrowsDuplicate_WhenCodeExists()
    {
        var repo = new FakeCustomerRepository();
        repo.Customers.Add(new Customer { Id = 1, CustomerCode = "CUST-001", Name = "Existing" });
        var service = new CustomerService(repo, new FakeDocNumberGen());

        await Assert.ThrowsAsync<DuplicateKeyException>(() => service.CreateCustomerAsync(new CustomerUpsertDto
        {
            CustomerCode = "CUST-001",
            Name = "Another"
        }));
    }
}

