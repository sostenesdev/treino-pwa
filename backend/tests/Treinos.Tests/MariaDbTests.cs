using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Treinos.Application;
using Treinos.Domain;
using Treinos.Infrastructure;
using Treinos.Infrastructure.Identity;
namespace Treinos.Tests;
public sealed class MariaDbFactAttribute:FactAttribute{public MariaDbFactAttribute(){if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TREINOS_TEST_CONNECTION")))Skip="Requer o pod de integração: deploy/scripts/test.sh";}}
public sealed class MariaDbTests
{
    private static ServiceProvider Services(){DefaultTypeMap.MatchNamesWithUnderscores=true;var services=new ServiceCollection();services.AddLogging();services.AddSingleton(new Database(Environment.GetEnvironmentVariable("TREINOS_TEST_CONNECTION")!));services.AddDataProtection();services.AddIdentityCore<AppUser>(o=>{o.Password.RequiredLength=12;o.Password.RequireDigit=false;o.Password.RequireNonAlphanumeric=false;o.Password.RequireUppercase=false;o.Password.RequireLowercase=false;o.User.RequireUniqueEmail=true;o.Lockout.MaxFailedAccessAttempts=5;}).AddUserStore<DapperUserStore>().AddDefaultTokenProviders();services.AddScoped<IUserClaimsPrincipalFactory<AppUser>,PrincipalFactory>();return services.BuildServiceProvider();}
    private static async Task<AppUser> Create(UserManager<AppUser> manager){var u=new AppUser{Id=Guid.NewGuid().ToString(),UserName=$"{Guid.NewGuid():N}@example.invalid",DisplayName="Teste",AppRole="common"};u.Email=u.UserName;Assert.True((await manager.CreateAsync(u,"Senha teste longa 2026")).Succeeded);return u;}
    [MariaDbFact] public async Task IdentityNormalizesAndCreatesSyncState(){using var services=Services();using var scope=services.CreateScope();var manager=scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();var u=await Create(manager);Assert.Equal(u.Id,(await manager.FindByEmailAsync(u.Email!.ToUpperInvariant()))!.Id);await using var db=await services.GetRequiredService<Database>().Open();var hash=await db.ExecuteScalarAsync<string>("SELECT password_hash FROM app_users WHERE id=@Id",new{u.Id});Assert.NotEqual("Senha teste longa 2026",hash);Assert.Equal(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM user_sync_state WHERE user_id=@Id",new{u.Id}));}
    [MariaDbFact] public async Task StoreRejectsLostConcurrentUpdate(){using var services=Services();using var scope=services.CreateScope();var manager=scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();var u=await Create(manager);var store=scope.ServiceProvider.GetRequiredService<IUserStore<AppUser>>();var a=(await store.FindByIdAsync(u.Id,default))!;var b=(await store.FindByIdAsync(u.Id,default))!;a.DisplayName="Primeiro";b.DisplayName="Segundo";Assert.True((await store.UpdateAsync(a,default)).Succeeded);var failed=await store.UpdateAsync(b,default);Assert.Contains(failed.Errors,e=>e.Code=="ConcurrencyFailure");}
    [MariaDbFact] public async Task ConcurrentPasswordResetHasSingleWinnerAndRevokesOldStamp(){using var services=Services();using var scope=services.CreateScope();var manager=scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();var u=await Create(manager);var original=u.SecurityStamp;var token=await manager.GeneratePasswordResetTokenAsync(u);var a=(await manager.FindByIdAsync(u.Id))!;var b=(await manager.FindByIdAsync(u.Id))!;var results=await Task.WhenAll(manager.ResetPasswordAsync(a,token,"Nova senha teste longa 2026"),manager.ResetPasswordAsync(b,token,"Outra senha teste longa 2026"));Assert.Single(results,r=>r.Succeeded);var latest=(await manager.FindByIdAsync(u.Id))!;Assert.NotEqual(original,latest.SecurityStamp);Assert.False((await manager.ResetPasswordAsync(latest,token,"Senha reutilizada longa 2026")).Succeeded);}
    [MariaDbFact] public async Task SyncRetryAfterDeleteReturnsOriginalReceipt(){using var services=Services();using var scope=services.CreateScope();var u=await Create(scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>());var training=new TrainingService(services.GetRequiredService<Database>(),new Current(u.Id));var ex=await training.SaveExercise(null,new("Exercício","Grupo",null,"reps","external","total",null),default);var s=new SessionEntry(Guid.NewGuid().ToString(),null,"Treino",DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)).ToString("yyyy-MM-dd"),"America/Sao_Paulo","completed","quick",null,[new(Guid.NewGuid().ToString(),ex.Id,1,ex.Name,"reps","external","total","total","completed",[])]);var op=new SyncOperation(Guid.NewGuid().ToString(),Guid.NewGuid().ToString(),1,s.Id,"create",0,s);var ack=await training.Apply(op,default);Assert.Equal(ack,await training.Apply(op,default));await training.Apply(new(Guid.NewGuid().ToString(),op.DeviceId,1,s.Id,"delete",1,null),default);Assert.Equal(ack,await training.Apply(op,default));Assert.True((await training.Session(s.Id,default))!.Deleted);await Assert.ThrowsAsync<InvalidOperationException>(()=>training.Apply(op with {Snapshot=s with {Notes="payload alterado"}},default));}
    [MariaDbFact] public async Task CrossOwnerReferencesNeverCreateSession(){using var services=Services();using var scope=services.CreateScope();var manager=scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();var a=await Create(manager);var b=await Create(manager);var db=services.GetRequiredService<Database>();var owner=new TrainingService(db,new Current(a.Id));var other=new TrainingService(db,new Current(b.Id));var ex=await owner.SaveExercise(null,new("Privado","Grupo",null,"reps","external","total",null),default);var s=new SessionEntry(Guid.NewGuid().ToString(),null,"Inválido",DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)).ToString("yyyy-MM-dd"),"America/Sao_Paulo","completed","quick",null,[new(Guid.NewGuid().ToString(),ex.Id,1,ex.Name,"reps","external","total","total","completed",[])]);await Assert.ThrowsAsync<ResourceNotFoundException>(()=>other.Apply(new(Guid.NewGuid().ToString(),Guid.NewGuid().ToString(),1,s.Id,"create",0,s),default));Assert.Null(await other.Session(s.Id,default));}
    [MariaDbFact] public async Task FeedRetentionExpiresCursorButPreservesRetryReceipt()
    {
        using var services=Services(); using var scope=services.CreateScope();
        var u=await Create(scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>());
        var database=services.GetRequiredService<Database>(); var training=new TrainingService(database,new Current(u.Id));
        var ex=await training.SaveExercise(null,new("Retenção","Grupo",null,"reps","external","total",null),default);
        var s=new SessionEntry(Guid.NewGuid().ToString(),null,"Treino",DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)).ToString("yyyy-MM-dd"),"America/Sao_Paulo","completed","quick",null,[new(Guid.NewGuid().ToString(),ex.Id,1,ex.Name,"reps","external","total","total","completed",[])]);
        var op=new SyncOperation(Guid.NewGuid().ToString(),Guid.NewGuid().ToString(),1,s.Id,"create",0,s);
        var ack=await training.Apply(op,default);
        await using(var db=await database.Open()) await db.ExecuteAsync("UPDATE sync_changes SET created_at_utc=DATE_SUB(UTC_TIMESTAMP(6),INTERVAL 2 DAY) WHERE user_id=@Id",new {u.Id});
        await RetentionMaintenance.Prune(database,DateTime.UtcNow.AddDays(-1),default);
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>training.Changes(0,100,default));
        Assert.Equal("SYNC_CURSOR_EXPIRED",error.Message);
        Assert.Equal(ack,await training.Apply(op,default));
        Assert.Empty((await training.Changes(ack.Revision,100,default)).Changes);
    }
    [MariaDbFact] public async Task OnlineCommandsReuseSyncVersioningAndReceipts()
    {
        using var services=Services(); using var scope=services.CreateScope();
        var u=await Create(scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>());
        var database=services.GetRequiredService<Database>(); var training=new TrainingService(database,new Current(u.Id)); var commands=new SessionCommands(training);
        var ex=await training.SaveExercise(null,new("Comando","Grupo",null,"reps","external","total",null),default);
        var s=new SessionEntry(Guid.NewGuid().ToString(),null,"Online",DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-120)).ToString("yyyy-MM-dd"),"America/Sao_Paulo","in_progress","quick",null,[new(Guid.NewGuid().ToString(),ex.Id,1,ex.Name,"reps","external","total","total","planned",[])]);
        var device=Guid.NewGuid().ToString();
        var created=await commands.Execute("create",s.Id,new(Guid.NewGuid().ToString(),device,0,s),null,null,default);
        var complete=new SessionCommand(Guid.NewGuid().ToString(),device,created.Version,s with {Exercises=s.Exercises.Select(e=>e with {CompletionStatus="completed"}).ToList()});
        var ack=await commands.Execute("complete",s.Id,complete,null,null,default);
        Assert.Equal(ack,await commands.Execute("complete",s.Id,complete,null,null,default));
        Assert.Equal(2,(await training.Session(s.Id,default))!.RowVersion);
        await Assert.ThrowsAsync<ArgumentException>(()=>commands.Execute("remove-exercise",s.Id,complete,Guid.NewGuid().ToString(),null,default));
        Assert.Empty((await training.Bootstrap(default)).Sessions);
        Assert.Single(await training.Sessions(default,DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-150)).ToString("yyyy-MM-dd"),DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),1));
        Assert.Empty(await training.Sessions(default,DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-150)).ToString("yyyy-MM-dd"),DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),2));
    }
    [MariaDbFact] public async Task AdministratorScopesHierarchyToOwnerAndPreservesExerciseSnapshots()
    {
        using var services=Services();using var scope=services.CreateScope();
        var manager=scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var admin=await Create(manager);admin.AppRole="administrator";Assert.True((await manager.UpdateAsync(admin)).Succeeded);
        var owner=await Create(manager);var stranger=await Create(manager);var database=services.GetRequiredService<Database>();
        var selected=new TrainingService(database,new AdministratorCurrent(admin.Id),new Owner(owner.Id));
        var actorService=new TrainingService(database,new AdministratorCurrent(admin.Id));
        var ownService=new TrainingService(database,new Current(owner.Id));
        var forbidden=new TrainingService(database,new Current(stranger.Id),new Owner(owner.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>forbidden.Exercises(default));
        var longInstructions=new string('a',70000)+"\nOrientações completas";
        var first=await selected.SaveExercise(null,new("Exercício um",Equipment:"Equipamento livre",Instructions:longInstructions),default);
        var second=await selected.SaveExercise(null,new("Exercício dois",MeasurementType:"duration",LoadKind:"bodyweight"),default);
        var plan1=await selected.SavePlan(null,new("Treino um",null),default);
        var plan2=await selected.SavePlan(null,new("Treino dois",null),default);
        List<TemplateItemInput> items=[new(first.Id,1,3,8,12,null,null,60,90,"total",[]),new(second.Id,2,3,null,null,30,45,60,90,"total",[])];
        foreach(var plan in new[]{plan1,plan2})foreach(var code in new[]{"A","B"})await selected.SaveTemplate(null,plan,new(code,$"Ficha {code}",code=="A"?1:2,null,items),default);
        var plans=await ownService.Plans(default);Assert.Equal(2,plans.Count);Assert.All(plans,p=>{Assert.Equal(2,p.Templates.Count);Assert.All(p.Templates,t=>Assert.Equal(2,t.Items.Count));});
        Assert.Empty(await actorService.Exercises(default));Assert.Empty(await actorService.Plans(default));
        var session=new SessionEntry(Guid.NewGuid().ToString(),null,"Registro",DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)).ToString("yyyy-MM-dd"),"America/Sao_Paulo","completed","quick",null,[new(Guid.NewGuid().ToString(),first.Id,1,first.Name,"reps","external","total","total","completed",[],first.Equipment,first.Instructions)]);
        await selected.Apply(new(Guid.NewGuid().ToString(),Guid.NewGuid().ToString(),1,session.Id,"create",0,session),default);
        var edited=await selected.SaveExercise(first.Id,new("Nome editado",Equipment:"Novo equipamento",Instructions:"Nova instrução",ExpectedVersion:first.RowVersion),default);
        Assert.Equal("Novo equipamento",edited.Equipment);Assert.Equal("reps",edited.MeasurementType);
        Assert.Equal(longInstructions,(await ownService.Session(session.Id,default))!.Exercises[0].InstructionsSnapshot);
        var duration=await selected.SaveExercise(second.Id,new("Tempo editado",Equipment:"Tapete",Instructions:longInstructions,ExpectedVersion:second.RowVersion),default);
        Assert.Equal("duration",duration.MeasurementType);Assert.Equal("bodyweight",duration.LoadKind);
        Assert.Empty((await actorService.Changes(0,100,default)).Changes);Assert.NotEmpty((await ownService.Changes(0,100,default)).Changes);
        var foreignExercise=await new TrainingService(database,new Current(stranger.Id)).SaveExercise(null,new("Outro dono"),default);
        await Assert.ThrowsAsync<ArgumentException>(()=>selected.SaveTemplate(null,plan1,new("C","Inválida",3,null,[items[0] with {ExerciseId=foreignExercise.Id}]),default));
        await selected.ArchiveDefinition("template",plans[0].Templates[0].Id,plans[0].Templates[0].RowVersion,default);
        await selected.ArchiveDefinition("plan",plan2,1,default);
        Assert.Single(await ownService.Plans(default));
        Assert.Equal(duration.RowVersion+1,(await selected.DeleteExercise(second.Id,duration.RowVersion,default)).Version);
        await manager.DeleteAsync(admin);
    }
    [MariaDbFact] public async Task UserCrudEnforcesRolesVersionsSessionRevocationAndLogicalDeletion()
    {
        using var services=Services();using var scope=services.CreateScope();var manager=scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var database=services.GetRequiredService<Database>();var principal=scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<AppUser>>();
        var admin=(await manager.FindByEmailAsync("ci@example.invalid"))!;
        var accounts=new AccountService(manager,principal,new AdministratorCurrent(admin.Id),database);
        var created=await accounts.CreateUser(new("Novo usuário",$"{Guid.NewGuid():N}@example.invalid","administrator","UTC","Senha inicial teste 2026"));
        Assert.Equal("administrator",created.Role);Assert.Equal("UTC",created.TimeZone);Assert.True(created.MustChangePassword);
        Assert.Contains(await accounts.Users(),u=>u.Id==created.Id);
        var stamp=(await manager.FindByIdAsync(created.Id))!.SecurityStamp!;
        var changed=await accounts.UpdateUser(created.Id,new("Alterado",created.Email,"common","America/Sao_Paulo",ExpectedVersion:created.RowVersion));
        Assert.Equal(created.RowVersion+1,changed.RowVersion);Assert.Null(await accounts.ValidateSession(created.Id,stamp,"administrator"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>accounts.UpdateUser(created.Id,new("Versão antiga",created.Email,ExpectedVersion:created.RowVersion)));
        var commonAccounts=new AccountService(manager,principal,new Current(created.Id),database);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>commonAccounts.CreateUser(new("Intruso",$"{Guid.NewGuid():N}@example.invalid",InitialPassword:"Senha inicial teste 2026")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>commonAccounts.Users());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>commonAccounts.UpdateUser(created.Id,new("Promoção",created.Email,"administrator",ExpectedVersion:changed.RowVersion)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>commonAccounts.User(admin.Id));
        var training=new TrainingService(database,new Current(created.Id));var exercise=await training.SaveExercise(null,new("Preservado"),default);
        await accounts.DeleteUser(created.Id,changed.RowVersion);
        Assert.Null(await manager.FindByIdAsync(created.Id));Assert.Null(await accounts.Login(created.Email,"Senha inicial teste 2026"));
        Assert.DoesNotContain(await accounts.Users(),u=>u.Id==created.Id);
        await using var db=await database.Open();Assert.Equal(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM exercises WHERE id=@Id",new{exercise.Id}));
        // The CI account remains the sole administrator after temporary admins are removed.
        Assert.Equal(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM app_users WHERE app_role='administrator' AND deleted_at_utc IS NULL"));
        var last=(await accounts.User(admin.Id))!;
        await Assert.ThrowsAsync<ArgumentException>(()=>accounts.DeleteUser(admin.Id,last.RowVersion));
        await Assert.ThrowsAsync<ArgumentException>(()=>accounts.UpdateUser(admin.Id,new(last.DisplayName,last.Email,"common",ExpectedVersion:last.RowVersion)));
    }
    private sealed class AdministratorCurrent(string id):ICurrentUser{public string Id=>id;public bool IsAdministrator=>true;}
    private sealed class Owner(string id):ITrainingOwner{public string OwnerId=>id;}
    private sealed class Current(string id):ICurrentUser{public string Id=>id;}
}
