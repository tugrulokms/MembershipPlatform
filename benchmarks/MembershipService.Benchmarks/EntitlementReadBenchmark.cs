using BenchmarkDotNet.Attributes;
using MembershipService.Application.Common;
using MembershipService.Application.Entitlements.Get;
using MembershipService.Application.Interfaces;
using MembershipService.Infrastructure.Caching;
using MembershipService.Infrastructure.Persistence;
using MembershipService.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MembershipService.Benchmarks
{
    [MemoryDiagnoser]  // also measures allocations, not just time
    public class EntitlementReadBenchmark
    {
        // Fields — hold whatever you need across benchmark calls
        private GetEntitlementsHandler _handler = null!;
        private IEntitlementCacheService _cacheService = null!;
        private ServiceProvider _serviceProvider = null!;

        [GlobalSetup]
        public async Task Setup()
        {
            var services = new ServiceCollection();

            services.AddStackExchangeRedisCache(options => options.Configuration = "localhost:6379");
            services.AddDbContext<MembershipDbContext>(options =>
        options.UseNpgsql("Host=localhost;Port=5432;Database=membership_db;Username=app;Password=app"));

            services.AddScoped<IEntitlementRepository, EntitlementRepository>();
            services.AddScoped<IEntitlementCacheService, EntitlementCacheService>();
            services.AddScoped<ICorrelationIdProvider, CorrelationIdProvider>();
            
            services.AddScoped<GetEntitlementsHandler>();

            _serviceProvider = services.BuildServiceProvider();

            _handler = _serviceProvider.GetRequiredService<GetEntitlementsHandler>();
            _cacheService = _serviceProvider.GetRequiredService<IEntitlementCacheService>();

            await _handler.HandleAsync(1);
            // Runs ONCE before all benchmarks.
            // Wire DI, resolve dependencies, seed data.
        }

        [IterationSetup(Targets = [nameof(CacheMiss)])]
        public void InvalidateCache()
        {
            // Runs before EACH iteration of CacheMiss only.
            // Invalidate so every iteration is a real miss.
            _cacheService.InvalidateAsync(1).GetAwaiter().GetResult();
        }

        [Benchmark]
        public async Task CacheHit()
        {
            // Cache is warm. Measures Redis deserialization only.
            await _handler.HandleAsync(1);
        }

        [Benchmark]
        public async Task CacheMiss()
        {
            // Cache was just invalidated. Measures DB + cache write.
            await _handler.HandleAsync(1);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            // Runs ONCE after all benchmarks.
            // Invalidate cache, dispose anything that needs it.
            _serviceProvider.Dispose();
        }
    }

}