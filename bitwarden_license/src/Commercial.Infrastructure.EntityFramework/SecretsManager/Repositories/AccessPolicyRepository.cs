﻿using AutoMapper;
using Bit.Core.Enums;
using Bit.Core.SecretsManager.Enums.AccessPolicies;
using Bit.Core.SecretsManager.Models.Data;
using Bit.Core.SecretsManager.Models.Data.AccessPolicyUpdates;
using Bit.Core.SecretsManager.Repositories;
using Bit.Infrastructure.EntityFramework.Repositories;
using Bit.Infrastructure.EntityFramework.SecretsManager.Discriminators;
using Bit.Infrastructure.EntityFramework.SecretsManager.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;


namespace Bit.Commercial.Infrastructure.EntityFramework.SecretsManager.Repositories;

public class AccessPolicyRepository : BaseEntityFrameworkRepository, IAccessPolicyRepository
{
    public AccessPolicyRepository(IServiceScopeFactory serviceScopeFactory, IMapper mapper) : base(serviceScopeFactory,
        mapper)
    {
    }

    public async Task<List<Core.SecretsManager.Entities.BaseAccessPolicy>> CreateManyAsync(List<Core.SecretsManager.Entities.BaseAccessPolicy> baseAccessPolicies)
    {
        await using var scope = ServiceScopeFactory.CreateAsyncScope();
        var dbContext = GetDatabaseContext(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        var serviceAccountIds = new List<Guid>();
        foreach (var baseAccessPolicy in baseAccessPolicies)
        {
            baseAccessPolicy.SetNewId();
            switch (baseAccessPolicy)
            {
                case Core.SecretsManager.Entities.UserProjectAccessPolicy accessPolicy:
                    {
                        var entity =
                            Mapper.Map<UserProjectAccessPolicy>(accessPolicy);
                        await dbContext.AddAsync(entity);
                        break;
                    }
                case Core.SecretsManager.Entities.UserServiceAccountAccessPolicy accessPolicy:
                    {
                        var entity =
                            Mapper.Map<UserServiceAccountAccessPolicy>(accessPolicy);
                        await dbContext.AddAsync(entity);
                        break;
                    }
                case Core.SecretsManager.Entities.GroupProjectAccessPolicy accessPolicy:
                    {
                        var entity = Mapper.Map<GroupProjectAccessPolicy>(accessPolicy);
                        await dbContext.AddAsync(entity);
                        break;
                    }
                case Core.SecretsManager.Entities.GroupServiceAccountAccessPolicy accessPolicy:
                    {
                        var entity = Mapper.Map<GroupServiceAccountAccessPolicy>(accessPolicy);
                        await dbContext.AddAsync(entity);
                        break;
                    }
                case Core.SecretsManager.Entities.ServiceAccountProjectAccessPolicy accessPolicy:
                    {
                        var entity = Mapper.Map<ServiceAccountProjectAccessPolicy>(accessPolicy);
                        await dbContext.AddAsync(entity);
                        serviceAccountIds.Add(entity.ServiceAccountId!.Value);
                        break;
                    }
            }
        }

        if (serviceAccountIds.Count > 0)
        {
            var utcNow = DateTime.UtcNow;
            await dbContext.ServiceAccount
                .Where(sa => serviceAccountIds.Contains(sa.Id))
                .ExecuteUpdateAsync(setters => setters.SetProperty(sa => sa.RevisionDate, utcNow));
        }

        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return baseAccessPolicies;
    }

    public async Task<bool> AccessPolicyExists(Core.SecretsManager.Entities.BaseAccessPolicy baseAccessPolicy)
    {
        using var scope = ServiceScopeFactory.CreateScope();
        var dbContext = GetDatabaseContext(scope);
        switch (baseAccessPolicy)
        {
            case Core.SecretsManager.Entities.UserProjectAccessPolicy accessPolicy:
                {
                    var policy = await dbContext.UserProjectAccessPolicy
                        .Where(c => c.OrganizationUserId == accessPolicy.OrganizationUserId &&
                                    c.GrantedProjectId == accessPolicy.GrantedProjectId)
                        .FirstOrDefaultAsync();
                    return policy != null;
                }
            case Core.SecretsManager.Entities.GroupProjectAccessPolicy accessPolicy:
                {
                    var policy = await dbContext.GroupProjectAccessPolicy
                        .Where(c => c.GroupId == accessPolicy.GroupId &&
                                    c.GrantedProjectId == accessPolicy.GrantedProjectId)
                        .FirstOrDefaultAsync();
                    return policy != null;
                }
            case Core.SecretsManager.Entities.ServiceAccountProjectAccessPolicy accessPolicy:
                {
                    var policy = await dbContext.ServiceAccountProjectAccessPolicy
                        .Where(c => c.ServiceAccountId == accessPolicy.ServiceAccountId &&
                                    c.GrantedProjectId == accessPolicy.GrantedProjectId)
                        .FirstOrDefaultAsync();
                    return policy != null;
                }
            case Core.SecretsManager.Entities.UserServiceAccountAccessPolicy accessPolicy:
                {
                    var policy = await dbContext.UserServiceAccountAccessPolicy
                        .Where(c => c.OrganizationUserId == accessPolicy.OrganizationUserId &&
                                    c.GrantedServiceAccountId == accessPolicy.GrantedServiceAccountId)
                        .FirstOrDefaultAsync();
                    return policy != null;
                }
            case Core.SecretsManager.Entities.GroupServiceAccountAccessPolicy accessPolicy:
                {
                    var policy = await dbContext.GroupServiceAccountAccessPolicy
                        .Where(c => c.GroupId == accessPolicy.GroupId &&
                                    c.GrantedServiceAccountId == accessPolicy.GrantedServiceAccountId)
                        .FirstOrDefaultAsync();
                    return policy != null;
                }
            default:
                throw new ArgumentException("Unsupported access policy type provided.", nameof(baseAccessPolicy));
        }
    }

    public async Task<Core.SecretsManager.Entities.BaseAccessPolicy?> GetByIdAsync(Guid id)
    {
        using var scope = ServiceScopeFactory.CreateScope();
        var dbContext = GetDatabaseContext(scope);
        var entity = await dbContext.AccessPolicies.Where(ap => ap.Id == id)
            .Include(ap => ((UserProjectAccessPolicy)ap).OrganizationUser.User)
            .Include(ap => ((UserProjectAccessPolicy)ap).GrantedProject)
            .Include(ap => ((GroupProjectAccessPolicy)ap).Group)
            .Include(ap => ((GroupProjectAccessPolicy)ap).GrantedProject)
            .Include(ap => ((ServiceAccountProjectAccessPolicy)ap).ServiceAccount)
            .Include(ap => ((ServiceAccountProjectAccessPolicy)ap).GrantedProject)
            .Include(ap => ((UserServiceAccountAccessPolicy)ap).OrganizationUser.User)
            .Include(ap => ((UserServiceAccountAccessPolicy)ap).GrantedServiceAccount)
            .Include(ap => ((GroupServiceAccountAccessPolicy)ap).Group)
            .Include(ap => ((GroupServiceAccountAccessPolicy)ap).GrantedServiceAccount)
            .FirstOrDefaultAsync();

        return entity == null ? null : MapToCore(entity);
    }

    public async Task ReplaceAsync(Core.SecretsManager.Entities.BaseAccessPolicy baseAccessPolicy)
    {
        using var scope = ServiceScopeFactory.CreateScope();
        var dbContext = GetDatabaseContext(scope);
        var entity = await dbContext.AccessPolicies.FindAsync(baseAccessPolicy.Id);
        if (entity != null)
        {
            dbContext.AccessPolicies.Attach(entity);
            entity.Write = baseAccessPolicy.Write;
            entity.Read = baseAccessPolicy.Read;
            entity.RevisionDate = baseAccessPolicy.RevisionDate;
            await dbContext.SaveChangesAsync();
        }
    }

    public async Task<IEnumerable<Core.SecretsManager.Entities.BaseAccessPolicy>> GetManyByGrantedProjectIdAsync(Guid id, Guid userId)
    {
        using var scope = ServiceScopeFactory.CreateScope();
        var dbContext = GetDatabaseContext(scope);

        var entities = await dbContext.AccessPolicies.Where(ap =>
                ((UserProjectAccessPolicy)ap).GrantedProjectId == id ||
                ((GroupProjectAccessPolicy)ap).GrantedProjectId == id ||
                ((ServiceAccountProjectAccessPolicy)ap).GrantedProjectId == id)
            .Include(ap => ((UserProjectAccessPolicy)ap).OrganizationUser.User)
            .Include(ap => ((GroupProjectAccessPolicy)ap).Group)
            .Include(ap => ((ServiceAccountProjectAccessPolicy)ap).ServiceAccount)
            .Select(ap => new
            {
                ap,
                CurrentUserInGroup = ap is GroupProjectAccessPolicy &&
                                     ((GroupProjectAccessPolicy)ap).Group.GroupUsers.Any(g =>
                                         g.OrganizationUser.User.Id == userId),
            })
            .ToListAsync();

        return entities.Select(e => MapToCore(e.ap, e.CurrentUserInGroup));
    }

    public async Task DeleteAsync(Guid id)
    {
        using var scope = ServiceScopeFactory.CreateScope();
        var dbContext = GetDatabaseContext(scope);
        var entity = await dbContext.AccessPolicies.FindAsync(id);
        if (entity != null)
        {
            if (entity is ServiceAccountProjectAccessPolicy serviceAccountProjectAccessPolicy)
            {
                var serviceAccount =
                    await dbContext.ServiceAccount.FindAsync(serviceAccountProjectAccessPolicy.ServiceAccountId);
                if (serviceAccount != null)
                {
                    serviceAccount.RevisionDate = DateTime.UtcNow;
                }
            }

            dbContext.Remove(entity);
            await dbContext.SaveChangesAsync();
        }
    }

    public async Task<PeopleGrantees> GetPeopleGranteesAsync(Guid organizationId, Guid currentUserId)
    {
        using var scope = ServiceScopeFactory.CreateScope();
        var dbContext = GetDatabaseContext(scope);

        var userGrantees = await dbContext.OrganizationUsers
            .Where(ou =>
                ou.OrganizationId == organizationId &&
                ou.AccessSecretsManager &&
                ou.Status == OrganizationUserStatusType.Confirmed)
            .Include(ou => ou.User)
            .Select(ou => new
                UserGrantee
            {
                OrganizationUserId = ou.Id,
                Name = ou.User.Name,
                Email = ou.User.Email,
                CurrentUser = ou.UserId == currentUserId
            }).ToListAsync();

        var groupGrantees = await dbContext.Groups
            .Where(g => g.OrganizationId == organizationId)
            .Include(g => g.GroupUsers)
            .Select(g => new GroupGrantee
            {
                GroupId = g.Id,
                Name = g.Name,
                CurrentUserInGroup = g.GroupUsers.Any(gu =>
                    gu.OrganizationUser.User.Id == currentUserId)
            }).ToListAsync();

        return new PeopleGrantees { UserGrantees = userGrantees, GroupGrantees = groupGrantees };
    }

    public async Task<IEnumerable<Core.SecretsManager.Entities.BaseAccessPolicy>>
        GetPeoplePoliciesByGrantedProjectIdAsync(Guid id, Guid userId)
    {
        using var scope = ServiceScopeFactory.CreateScope();
        var dbContext = GetDatabaseContext(scope);

        var entities = await dbContext.AccessPolicies.Where(ap =>
                ap.Discriminator != AccessPolicyDiscriminator.ServiceAccountProject &&
                (((UserProjectAccessPolicy)ap).GrantedProjectId == id ||
                 ((GroupProjectAccessPolicy)ap).GrantedProjectId == id))
            .Include(ap => ((UserProjectAccessPolicy)ap).OrganizationUser.User)
            .Include(ap => ((GroupProjectAccessPolicy)ap).Group)
            .Select(ap => new
            {
                ap,
                CurrentUserInGroup = ap is GroupProjectAccessPolicy &&
                                     ((GroupProjectAccessPolicy)ap).Group.GroupUsers.Any(g =>
                                         g.OrganizationUser.UserId == userId),
            })
            .ToListAsync();

        return entities.Select(e => MapToCore(e.ap, e.CurrentUserInGroup));
    }

    public async Task<IEnumerable<Core.SecretsManager.Entities.BaseAccessPolicy>> ReplaceProjectPeopleAsync(
        ProjectPeopleAccessPolicies peopleAccessPolicies, Guid userId)
    {
        using var scope = ServiceScopeFactory.CreateScope();
        var dbContext = GetDatabaseContext(scope);
        var peoplePolicyEntities = await dbContext.AccessPolicies.Where(ap =>
            ap.Discriminator != AccessPolicyDiscriminator.ServiceAccountProject &&
            (((UserProjectAccessPolicy)ap).GrantedProjectId == peopleAccessPolicies.Id ||
             ((GroupProjectAccessPolicy)ap).GrantedProjectId == peopleAccessPolicies.Id)).ToListAsync();

        var userPolicyEntities =
            peoplePolicyEntities.Where(ap => ap.GetType() == typeof(UserProjectAccessPolicy)).ToList();
        var groupPolicyEntities =
            peoplePolicyEntities.Where(ap => ap.GetType() == typeof(GroupProjectAccessPolicy)).ToList();


        if (peopleAccessPolicies.UserAccessPolicies == null || !peopleAccessPolicies.UserAccessPolicies.Any())
        {
            dbContext.RemoveRange(userPolicyEntities);
        }
        else
        {
            foreach (var userPolicyEntity in userPolicyEntities.Where(entity =>
                         peopleAccessPolicies.UserAccessPolicies.All(ap =>
                             ((Core.SecretsManager.Entities.UserProjectAccessPolicy)ap).OrganizationUserId !=
                             ((UserProjectAccessPolicy)entity).OrganizationUserId)))
            {
                dbContext.Remove(userPolicyEntity);
            }
        }

        if (peopleAccessPolicies.GroupAccessPolicies == null || !peopleAccessPolicies.GroupAccessPolicies.Any())
        {
            dbContext.RemoveRange(groupPolicyEntities);
        }
        else
        {
            foreach (var groupPolicyEntity in groupPolicyEntities.Where(entity =>
                         peopleAccessPolicies.GroupAccessPolicies.All(ap =>
                             ((Core.SecretsManager.Entities.GroupProjectAccessPolicy)ap).GroupId !=
                             ((GroupProjectAccessPolicy)entity).GroupId)))
            {
                dbContext.Remove(groupPolicyEntity);
            }
        }

        await UpsertPeoplePoliciesAsync(dbContext,
            peopleAccessPolicies.ToBaseAccessPolicies().Select(MapToEntity).ToList(), userPolicyEntities,
            groupPolicyEntities);

        await dbContext.SaveChangesAsync();
        return await GetPeoplePoliciesByGrantedProjectIdAsync(peopleAccessPolicies.Id, userId);
    }

    public async Task<IEnumerable<Core.SecretsManager.Entities.BaseAccessPolicy>>
        GetPeoplePoliciesByGrantedServiceAccountIdAsync(Guid id, Guid userId)
    {
        using var scope = ServiceScopeFactory.CreateScope();
        var dbContext = GetDatabaseContext(scope);

        var entities = await dbContext.AccessPolicies.Where(ap =>
                ((UserServiceAccountAccessPolicy)ap).GrantedServiceAccountId == id ||
                ((GroupServiceAccountAccessPolicy)ap).GrantedServiceAccountId == id)
            .Include(ap => ((UserServiceAccountAccessPolicy)ap).OrganizationUser.User)
            .Include(ap => ((GroupServiceAccountAccessPolicy)ap).Group)
            .Select(ap => new
            {
                ap,
                CurrentUserInGroup = ap is GroupServiceAccountAccessPolicy &&
                                     ((GroupServiceAccountAccessPolicy)ap).Group.GroupUsers.Any(g =>
                                         g.OrganizationUser.UserId == userId)
            })
            .ToListAsync();

        return entities.Select(e => MapToCore(e.ap, e.CurrentUserInGroup));
    }

    public async Task<IEnumerable<Core.SecretsManager.Entities.BaseAccessPolicy>> ReplaceServiceAccountPeopleAsync(
        ServiceAccountPeopleAccessPolicies peopleAccessPolicies, Guid userId)
    {
        using var scope = ServiceScopeFactory.CreateScope();
        var dbContext = GetDatabaseContext(scope);
        var peoplePolicyEntities = await dbContext.AccessPolicies.Where(ap =>
            ((UserServiceAccountAccessPolicy)ap).GrantedServiceAccountId == peopleAccessPolicies.Id ||
            ((GroupServiceAccountAccessPolicy)ap).GrantedServiceAccountId == peopleAccessPolicies.Id).ToListAsync();

        var userPolicyEntities =
            peoplePolicyEntities.Where(ap => ap.GetType() == typeof(UserServiceAccountAccessPolicy)).ToList();
        var groupPolicyEntities =
            peoplePolicyEntities.Where(ap => ap.GetType() == typeof(GroupServiceAccountAccessPolicy)).ToList();


        if (peopleAccessPolicies.UserAccessPolicies == null || !peopleAccessPolicies.UserAccessPolicies.Any())
        {
            dbContext.RemoveRange(userPolicyEntities);
        }
        else
        {
            foreach (var userPolicyEntity in userPolicyEntities.Where(entity =>
                         peopleAccessPolicies.UserAccessPolicies.All(ap =>
                             ((Core.SecretsManager.Entities.UserServiceAccountAccessPolicy)ap).OrganizationUserId !=
                             ((UserServiceAccountAccessPolicy)entity).OrganizationUserId)))
            {
                dbContext.Remove(userPolicyEntity);
            }
        }

        if (peopleAccessPolicies.GroupAccessPolicies == null || !peopleAccessPolicies.GroupAccessPolicies.Any())
        {
            dbContext.RemoveRange(groupPolicyEntities);
        }
        else
        {
            foreach (var groupPolicyEntity in groupPolicyEntities.Where(entity =>
                         peopleAccessPolicies.GroupAccessPolicies.All(ap =>
                             ((Core.SecretsManager.Entities.GroupServiceAccountAccessPolicy)ap).GroupId !=
                             ((GroupServiceAccountAccessPolicy)entity).GroupId)))
            {
                dbContext.Remove(groupPolicyEntity);
            }
        }

        await UpsertPeoplePoliciesAsync(dbContext,
            peopleAccessPolicies.ToBaseAccessPolicies().Select(MapToEntity).ToList(), userPolicyEntities,
            groupPolicyEntities);
        await dbContext.SaveChangesAsync();
        return await GetPeoplePoliciesByGrantedServiceAccountIdAsync(peopleAccessPolicies.Id, userId);
    }

    public async Task<ServiceAccountGrantedPolicies?> GetServiceAccountGrantedPoliciesAsync(Guid serviceAccountId)
    {
        await using var scope = ServiceScopeFactory.CreateAsyncScope();
        var dbContext = GetDatabaseContext(scope);
        var entities = await dbContext.ServiceAccountProjectAccessPolicy
            .Where(ap => ap.ServiceAccountId == serviceAccountId)
            .Include(ap => ap.ServiceAccount)
            .Include(ap => ap.GrantedProject)
            .ToListAsync();

        if (entities.Count == 0)
        {
            return null;
        }
        return new ServiceAccountGrantedPolicies(serviceAccountId, entities.Select(MapToCore).ToList());
    }

    public async Task<ServiceAccountGrantedPoliciesPermissionDetails?>
        GetServiceAccountGrantedPoliciesPermissionDetailsAsync(Guid serviceAccountId, Guid userId,
            AccessClientType accessClientType)
    {
        await using var scope = ServiceScopeFactory.CreateAsyncScope();
        var dbContext = GetDatabaseContext(scope);
        var accessPolicyQuery = dbContext.ServiceAccountProjectAccessPolicy
            .Where(ap => ap.ServiceAccountId == serviceAccountId)
            .Include(ap => ap.ServiceAccount)
            .Include(ap => ap.GrantedProject);

        var accessPoliciesPermissionDetails =
            await ToPermissionDetails(accessPolicyQuery, userId, accessClientType).ToListAsync();
        if (accessPoliciesPermissionDetails.Count == 0)
        {
            return null;
        }

        return new ServiceAccountGrantedPoliciesPermissionDetails
        {
            ServiceAccountId = serviceAccountId,
            OrganizationId = accessPoliciesPermissionDetails.First().AccessPolicy.GrantedProject!.OrganizationId,
            ProjectGrantedPolicies = accessPoliciesPermissionDetails
        };
    }

    public async Task UpdateServiceAccountGrantedPoliciesAsync(ServiceAccountGrantedPoliciesUpdates updates)
    {
        await using var scope = ServiceScopeFactory.CreateAsyncScope();
        var dbContext = GetDatabaseContext(scope);
        var currentAccessPolicies = await dbContext.ServiceAccountProjectAccessPolicy
            .Where(ap => ap.ServiceAccountId == updates.ServiceAccountId)
            .ToListAsync();

        if (currentAccessPolicies.Count != 0)
        {
            var projectIdsToDelete = updates.ProjectGrantedPolicyUpdates
                .Where(pu => pu.Operation == AccessPolicyOperation.Delete)
                .Select(pu => pu.AccessPolicy.GrantedProjectId!.Value)
                .ToList();

            var policiesToDelete = currentAccessPolicies
                .Where(entity => projectIdsToDelete.Contains(entity.GrantedProjectId!.Value))
                .ToList();

            dbContext.RemoveRange(policiesToDelete);
        }

        await UpsertServiceAccountGrantedPoliciesAsync(dbContext, currentAccessPolicies,
            updates.ProjectGrantedPolicyUpdates.Where(pu => pu.Operation != AccessPolicyOperation.Delete).ToList());
        await UpdateServiceAccountRevisionAsync(dbContext, updates.ServiceAccountId);
        await dbContext.SaveChangesAsync();
    }

    private static async Task UpsertPeoplePoliciesAsync(DatabaseContext dbContext,
        List<BaseAccessPolicy> policies, IReadOnlyCollection<AccessPolicy> userPolicyEntities,
        IReadOnlyCollection<AccessPolicy> groupPolicyEntities)
    {
        var currentDate = DateTime.UtcNow;
        foreach (var updatedEntity in policies)
        {
            var currentEntity = updatedEntity switch
            {
                UserProjectAccessPolicy ap => userPolicyEntities.FirstOrDefault(e =>
                    ((UserProjectAccessPolicy)e).OrganizationUserId == ap.OrganizationUserId),
                GroupProjectAccessPolicy ap => groupPolicyEntities.FirstOrDefault(e =>
                    ((GroupProjectAccessPolicy)e).GroupId == ap.GroupId),
                UserServiceAccountAccessPolicy ap => userPolicyEntities.FirstOrDefault(e =>
                    ((UserServiceAccountAccessPolicy)e).OrganizationUserId == ap.OrganizationUserId),
                GroupServiceAccountAccessPolicy ap => groupPolicyEntities.FirstOrDefault(e =>
                    ((GroupServiceAccountAccessPolicy)e).GroupId == ap.GroupId),
                _ => null
            };

            if (currentEntity != null)
            {
                dbContext.AccessPolicies.Attach(currentEntity);
                currentEntity.Read = updatedEntity.Read;
                currentEntity.Write = updatedEntity.Write;
                currentEntity.RevisionDate = currentDate;
            }
            else
            {
                updatedEntity.SetNewId();
                await dbContext.AddAsync(updatedEntity);
            }
        }
    }

    private async Task UpsertServiceAccountGrantedPoliciesAsync(DatabaseContext dbContext,
        IReadOnlyCollection<ServiceAccountProjectAccessPolicy> currentPolices,
        List<ServiceAccountProjectAccessPolicyUpdate> policyUpdates)
    {
        var currentDate = DateTime.UtcNow;
        foreach (var policyUpdate in policyUpdates)
        {
            var updatedEntity = MapToEntity(policyUpdate.AccessPolicy);
            var currentEntity = currentPolices.FirstOrDefault(e =>
                e.GrantedProjectId == policyUpdate.AccessPolicy.GrantedProjectId!.Value);

            switch (policyUpdate.Operation)
            {
                case AccessPolicyOperation.Create when currentEntity == null:
                    updatedEntity.SetNewId();
                    await dbContext.AddAsync(updatedEntity);
                    break;

                case AccessPolicyOperation.Update when currentEntity != null:
                    dbContext.AccessPolicies.Attach(currentEntity);
                    currentEntity.Read = updatedEntity.Read;
                    currentEntity.Write = updatedEntity.Write;
                    currentEntity.RevisionDate = currentDate;
                    break;
                default:
                    throw new InvalidOperationException("Policy updates failed due to unexpected state.");
            }
        }
    }

    private Core.SecretsManager.Entities.BaseAccessPolicy MapToCore(
        BaseAccessPolicy baseAccessPolicyEntity) =>
        baseAccessPolicyEntity switch
        {
            UserProjectAccessPolicy ap => Mapper.Map<Core.SecretsManager.Entities.UserProjectAccessPolicy>(ap),
            GroupProjectAccessPolicy ap => Mapper.Map<Core.SecretsManager.Entities.GroupProjectAccessPolicy>(ap),
            ServiceAccountProjectAccessPolicy ap => Mapper
                .Map<Core.SecretsManager.Entities.ServiceAccountProjectAccessPolicy>(ap),
            UserServiceAccountAccessPolicy ap =>
                Mapper.Map<Core.SecretsManager.Entities.UserServiceAccountAccessPolicy>(ap),
            GroupServiceAccountAccessPolicy ap => Mapper
                .Map<Core.SecretsManager.Entities.GroupServiceAccountAccessPolicy>(ap),
            _ => throw new ArgumentException("Unsupported access policy type")
        };

    private BaseAccessPolicy MapToEntity(Core.SecretsManager.Entities.BaseAccessPolicy baseAccessPolicy)
    {
        return baseAccessPolicy switch
        {
            Core.SecretsManager.Entities.UserProjectAccessPolicy accessPolicy => Mapper.Map<UserProjectAccessPolicy>(
                accessPolicy),
            Core.SecretsManager.Entities.UserServiceAccountAccessPolicy accessPolicy => Mapper
                .Map<UserServiceAccountAccessPolicy>(accessPolicy),
            Core.SecretsManager.Entities.GroupProjectAccessPolicy accessPolicy => Mapper.Map<GroupProjectAccessPolicy>(
                accessPolicy),
            Core.SecretsManager.Entities.GroupServiceAccountAccessPolicy accessPolicy => Mapper
                .Map<GroupServiceAccountAccessPolicy>(accessPolicy),
            Core.SecretsManager.Entities.ServiceAccountProjectAccessPolicy accessPolicy => Mapper
                .Map<ServiceAccountProjectAccessPolicy>(accessPolicy),
            _ => throw new ArgumentException("Unsupported access policy type")
        };
    }

    private Core.SecretsManager.Entities.BaseAccessPolicy MapToCore(
      BaseAccessPolicy baseAccessPolicyEntity, bool currentUserInGroup)
    {
        switch (baseAccessPolicyEntity)
        {
            case GroupProjectAccessPolicy ap:
                {
                    var mapped = Mapper.Map<Core.SecretsManager.Entities.GroupProjectAccessPolicy>(ap);
                    mapped.CurrentUserInGroup = currentUserInGroup;
                    return mapped;
                }
            case GroupServiceAccountAccessPolicy ap:
                {
                    var mapped = Mapper.Map<Core.SecretsManager.Entities.GroupServiceAccountAccessPolicy>(ap);
                    mapped.CurrentUserInGroup = currentUserInGroup;
                    return mapped;
                }
            default:
                return MapToCore(baseAccessPolicyEntity);
        }
    }

    private IQueryable<ServiceAccountProjectAccessPolicyPermissionDetails> ToPermissionDetails(
        IQueryable<ServiceAccountProjectAccessPolicy>
            query, Guid userId, AccessClientType accessClientType)
    {
        var permissionDetails = accessClientType switch
        {
            AccessClientType.NoAccessCheck => query.Select(ap => new ServiceAccountProjectAccessPolicyPermissionDetails
            {
                AccessPolicy =
                    Mapper.Map<Core.SecretsManager.Entities.ServiceAccountProjectAccessPolicy>(ap),
                HasPermission = true
            }),
            AccessClientType.User => query.Select(ap => new ServiceAccountProjectAccessPolicyPermissionDetails
            {
                AccessPolicy =
                    Mapper.Map<Core.SecretsManager.Entities.ServiceAccountProjectAccessPolicy>(ap),
                HasPermission =
                    (ap.GrantedProject.UserAccessPolicies.Any(p => p.OrganizationUser.UserId == userId && p.Write) ||
                     ap.GrantedProject.GroupAccessPolicies.Any(p =>
                         p.Group.GroupUsers.Any(gu => gu.OrganizationUser.UserId == userId && p.Write))) &&
                    (ap.ServiceAccount.UserAccessPolicies.Any(p => p.OrganizationUser.UserId == userId && p.Write) ||
                     ap.ServiceAccount.GroupAccessPolicies.Any(p =>
                         p.Group.GroupUsers.Any(gu => gu.OrganizationUser.UserId == userId && p.Write)))
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(accessClientType), accessClientType, null)
        };
        return permissionDetails;
    }

    private static async Task UpdateServiceAccountRevisionAsync(DatabaseContext dbContext, Guid serviceAccountId)
    {
        var entity = await dbContext.ServiceAccount.FindAsync(serviceAccountId);
        if (entity != null)
        {
            entity.RevisionDate = DateTime.UtcNow;
        }
    }
}
