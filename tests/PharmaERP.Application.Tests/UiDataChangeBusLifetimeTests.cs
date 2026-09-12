using System;
using Microsoft.Extensions.Logging.Abstractions;
using PharmaERP.Desktop.Common;
using Xunit;

namespace PharmaERP.Application.Tests;

/// <summary>
/// Unit tests verifying UiDataChangeBus subscription lifetime, subscriber isolation,
/// and ViewModel disposal behavior.
/// </summary>
public class UiDataChangeBusLifetimeTests
{
    private class TestSubscribingViewModel : ViewModelBase
    {
        private readonly IDisposable _busSubscription;
        public int InvocationCount { get; private set; }
        public UiDataChangeType? LastReceivedType { get; private set; }

        public TestSubscribingViewModel(IUiDataChangeBus bus)
        {
            _busSubscription = bus.Subscribe(evt =>
            {
                InvocationCount++;
                LastReceivedType = evt.ChangeType;
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _busSubscription.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private class TestParentLifetimeManager : ViewModelBase
    {
        private readonly TestSubscribingViewModel _child1;
        private readonly TestSubscribingViewModel _child2;

        public TestParentLifetimeManager(TestSubscribingViewModel child1, TestSubscribingViewModel child2)
        {
            _child1 = child1;
            _child2 = child2;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _child1.Dispose();
                _child2.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    [Fact]
    public void ActiveSubscriber_ReceivesPublishedEvent()
    {
        var bus = new UiDataChangeBus(NullLogger<UiDataChangeBus>.Instance);
        var vm = new TestSubscribingViewModel(bus);

        bus.Publish(UiDataChangeType.ProductChanged);

        Assert.Equal(1, vm.InvocationCount);
        Assert.Equal(UiDataChangeType.ProductChanged, vm.LastReceivedType);
    }

    [Fact]
    public void DisposedViewModel_Unsubscribes_AndDoesNotReceiveFutureEvents()
    {
        // Arrange
        var bus = new UiDataChangeBus(NullLogger<UiDataChangeBus>.Instance);
        var vm = new TestSubscribingViewModel(bus);

        bus.Publish(UiDataChangeType.ProductChanged);
        Assert.Equal(1, vm.InvocationCount);

        // Act - dispose owning ViewModel lifetime
        vm.Dispose();

        // Publish another event after disposal
        bus.Publish(UiDataChangeType.ProductChanged);
        bus.Publish(UiDataChangeType.StockChanged);

        // Assert - invocation count must remain 1
        Assert.Equal(1, vm.InvocationCount);
    }

    [Fact]
    public void ParentLifetimeDisposal_DisposesAllChildren_AndUnsubscribesFromBus()
    {
        // Arrange
        var bus = new UiDataChangeBus(NullLogger<UiDataChangeBus>.Instance);
        var child1 = new TestSubscribingViewModel(bus);
        var child2 = new TestSubscribingViewModel(bus);
        var parent = new TestParentLifetimeManager(child1, child2);

        bus.Publish(UiDataChangeType.SalePosted);
        Assert.Equal(1, child1.InvocationCount);
        Assert.Equal(1, child2.InvocationCount);

        // Act - dispose parent (mirroring MainWindow/MainWindowViewModel disposal on window close)
        parent.Dispose();

        // Publish event after parent disposal
        bus.Publish(UiDataChangeType.SalePosted);

        // Assert - neither child should have been invoked
        Assert.Equal(1, child1.InvocationCount);
        Assert.Equal(1, child2.InvocationCount);
    }

    [Fact]
    public void DatabaseConnectionChanged_EventIsReceivedBySubscribers()
    {
        var bus = new UiDataChangeBus(NullLogger<UiDataChangeBus>.Instance);
        var vm = new TestSubscribingViewModel(bus);

        bus.Publish(UiDataChangeType.DatabaseConnectionChanged);

        Assert.Equal(1, vm.InvocationCount);
        Assert.Equal(UiDataChangeType.DatabaseConnectionChanged, vm.LastReceivedType);
    }

    [Fact]
    public void SubscriberException_IsIsolated_AndRemainingSubscribersAreInvoked()
    {
        var bus = new UiDataChangeBus(NullLogger<UiDataChangeBus>.Instance);
        int successfulInvocations = 0;

        // Failing subscriber
        Action<UiDataChangeType> failingHandler = _ => throw new InvalidOperationException("Simulated subscriber error");
        bus.Subscribe(failingHandler);

        // Healthy subscriber
        Action<UiDataChangeType> healthyHandler = _ => successfulInvocations++;
        bus.Subscribe(healthyHandler);

        // Publish should not throw and healthy subscriber must run
        var exception = Record.Exception(() => bus.Publish(UiDataChangeType.ProductChanged));

        Assert.Null(exception);
        Assert.Equal(1, successfulInvocations);
    }
}
