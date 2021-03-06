using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using AutoMapper.QueryableExtensions;
using CodeFriendly.Core;
using CodeFriendly.Filtering;
using CodeFriendly.Filtering.EntityFramework;
using CodeFriendly.Filtering.Abstractions;
using CodeFriendly.Patch;
using CodeFriendly.ServiceModel.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace CodeFriendly.ServiceModel.EntityFramework
{
    public abstract class ServiceBase<TDbContext, TEntity, TDomain> : IService<TDomain>
        where TDomain: class, new()
        where TEntity: class, new()
        where TDbContext: DbContext
    {
        protected PatchMapper<TDomain, TEntity> PatchMapper { get; }
        protected IFilterParser FilterParser { get; }
        protected TDbContext Context { get; }
        

        protected ServiceBase(IFilterParser filterParser, TDbContext dbContext, PatchMapper<TDomain, TEntity> patchMapper)
        {
            PatchMapper = patchMapper;
            FilterParser = filterParser;
            Context = dbContext;
        }

        public virtual async Task<TDomain> Get(params object [] keyValues)
        {
            var entity = await GetEntity(keyValues);
            return PatchMapper.CreateSourceObject(entity);
        }

        public virtual async Task<IEnumerable<TDomain>> GetAll(IFilterOptions filterOptions)
        {
            return await GetAllInternal(filterOptions, OnGetAllBeforeQuery);
        }
        
        protected virtual async Task<IEnumerable<TDomain>> GetAllInternal(IFilterOptions filterOptions, 
            Func<IQueryable<TEntity>, IQueryable<TEntity>> beforeQuery)
        {
            var filter = FilterParser.Build<TDomain>(filterOptions);
            var contextSet = Context.Set<TEntity>().AsNoTracking();
            var query = beforeQuery !=null ? beforeQuery(contextSet) : contextSet;
            var items = await query
                .AsNoTracking()
                .WithFilter(filter, PatchMapper.Mapper)
                .ToListAsync();
            return items
                .Select(i=> PatchMapper.CreateSourceObject(i))
                .AsQueryable()
                .WithFilter(filter);
        }

        protected virtual async Task<IEnumerable<TDomain>> GetAllInternal(IFilterOptions filterOptions,
            Expression<Func<TEntity, bool>> expression)
        {
            return await GetAllInternal(filterOptions, q => q.Where(expression));
        }
        

        public virtual async Task<TDomain> Create(TDomain createModel)
        {
            var entity = PatchMapper.CreateDestinationObject(createModel);
            Context.Attach(entity);
            await OnCreatingBeforeSave(createModel, entity);
            await Context.SaveChangesAsync();
            var pks = GetPrimaryKey(PatchMapper.CreateSourceObject(entity));
            return await Get(pks);
        }
        
        public virtual async Task<IEnumerable<TDomain>> Create(IEnumerable<TDomain> createModels)
        {
            var ret = new List<TEntity>();
            foreach (var createModel in createModels)
            {
                var entity = PatchMapper.CreateDestinationObject(createModel);
                Context.Attach(entity);
                await OnCreatingBeforeSave(createModel, entity);
                ret.Add(entity);
            }
            
            await Context.SaveChangesAsync();
            var results = ret.Select(r=> new {data = r, domain = PatchMapper.CreateSourceObject(r)}).ToList();
            foreach (var r in results)
            {
                await OnCreated(r.domain, r.data);
            }
            return results.Select(r=>r.domain).ToList();
        }

        
        public async Task<TDomain> Patch<T>(T updateModel) where T : TDomain
        {
            var existing = await GetEntity(GetPrimaryKey(updateModel));
            var updated = PatchMapper.PatchDestinationObject(updateModel, existing);
            Context.Attach(updated);
            await OnPatchingBeforeSave(updateModel, updated);
            await Context.SaveChangesAsync();
            return PatchMapper.CreateSourceObject(updated);
        }


        protected virtual async Task<TEntity> GetEntity(params object [] keyValues)
        {
            return await Context.FindAsync<TEntity>(keyValues);
        }

        protected virtual Task OnCreatingBeforeSave(TDomain domain, TEntity entity)
        {
            return Task.CompletedTask;
        }
        
        protected virtual Task OnCreated(TDomain domain, TEntity entity)
        {
            return Task.CompletedTask;
        }
        
        protected virtual Task OnPatchingBeforeSave(TDomain domain, TEntity entity)
        {
            return Task.CompletedTask;
        }

        protected virtual IQueryable<TEntity> OnGetAllBeforeQuery(IQueryable<TEntity> query)
        {
            return query;
        }

        protected virtual object[] GetPrimaryKey(TDomain domainModel)
        {
            var pk = domainModel.GetValueOfPropertyWithAttribute<KeyAttribute, string>() 
                     ?? domainModel.GetPropertyValue("Id")?.ToString();
            if (pk == null) throw new InvalidOperationException("Could not find a default primary key using [Key] attribute or the Id field, try overriding GetPrimaryKey()");
            return new object[] {pk};
        }
    }
}