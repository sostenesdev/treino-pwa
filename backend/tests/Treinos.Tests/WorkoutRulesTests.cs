using Treinos.Domain;
namespace Treinos.Tests;
public sealed class WorkoutRulesTests
{
    private static ExerciseEntry Exercise(string measure="reps",string scope="total",params SetEntry[] sets)=>new(Guid.NewGuid().ToString(),Guid.NewGuid().ToString(),1,"Exercício",measure,"external","per_hand",scope,"completed",sets.ToList());
    private static SetEntry Set(int? reps,decimal? load,int? seconds=null,string side="both")=>new(){Id=Guid.NewGuid().ToString(),SetNumber=1,Side=side,Repetitions=reps,LoadKg=load,DurationSeconds=seconds,IsCompleted=true};
    private static SessionEntry Session(ExerciseEntry exercise)=>new(Guid.NewGuid().ToString(),null,"Treino","2026-09-29","America/Sao_Paulo","completed","detailed",null,[exercise]);
    private static readonly DateTimeOffset Now=new(2026,9,29,15,0,0,TimeSpan.Zero);
    [Fact] public void PartialVolumeDoesNotConvertMissingLoadToZero(){var a=Set(10,20);var b=Set(8,null);b.SetNumber=2;var p=ProgressRules.Calculate("2026-09-29",[Exercise(sets:[a,b])]);Assert.Equal(200m,p.VolumeKgReps);Assert.Equal(20m,p.MaxLoadKg);Assert.Equal(1,p.SetsWithLoad);Assert.Equal(2,p.EligibleSets);}
    [Fact] public void NoKnownLoadProducesNullVolume(){var p=ProgressRules.Calculate("2026-09-29",[Exercise(sets:[Set(10,null)])]);Assert.Null(p.VolumeKgReps);Assert.Null(p.MaxLoadKg);}
    [Fact] public void DurationHasNoRepetitionVolume(){var p=ProgressRules.Calculate("2026-09-29",[Exercise("duration",sets:[Set(null,null,30),Set(null,null,45)])]);Assert.Equal(75,p.DurationSeconds);Assert.Null(p.VolumeKgReps);}
    [Fact] public void UnilateralVolumeSumsSidesWithoutMultiplyingByTwo(){var p=ProgressRules.Calculate("2026-09-29",[Exercise(scope:"per_side",sets:[Set(10,8,side:"left"),Set(10,8,side:"right")])]);Assert.Equal(160m,p.VolumeKgReps);}
    [Fact] public void WarmupAndIncompleteSetsDoNotCount(){var a=Set(10,20);a.IsWarmup=true;var b=Set(10,30);b.IsCompleted=false;var p=ProgressRules.Calculate("2026-09-29",[Exercise(sets:[a,b])]);Assert.Equal(0,p.EligibleSets);Assert.Null(p.MaxLoadKg);}
    [Fact] public void CompletedSetAllowsMissingLoad(){WorkoutRules.Validate(Session(Exercise(sets:[Set(7,null)])),Now);}
    [Fact] public void DurationSetDoesNotRequireReps(){WorkoutRules.Validate(Session(Exercise("duration",sets:[Set(null,null,30)])),Now);}
    [Fact] public void RejectsWrongSideForUnilateral(){Assert.Throws<ArgumentException>(()=>WorkoutRules.Validate(Session(Exercise(scope:"per_side",sets:[Set(10,8)])),Now));}
    [Fact] public void RejectsNegativeLoad(){Assert.Throws<ArgumentException>(()=>WorkoutRules.Validate(Session(Exercise(sets:[Set(10,-1)])),Now));}
    [Fact] public void RejectsFutureDateInSessionTimezone(){Assert.Throws<ArgumentException>(()=>WorkoutRules.Validate(Session(Exercise(sets:[Set(10,null)])) with {PerformedOn="2026-09-30"},Now));}
    [Fact] public void QuickModeDoesNotInventSets(){WorkoutRules.Validate(Session(Exercise()) with {RecordingMode="quick"},Now);}
    [Fact] public void DetailedModeNeedsCompletedWorkSet(){Assert.Throws<ArgumentException>(()=>WorkoutRules.Validate(Session(Exercise()),Now));}
    [Fact] public void DomainHasNoInfrastructureReferences(){var references=typeof(WorkoutRules).Assembly.GetReferencedAssemblies();Assert.DoesNotContain(references,x=>x.Name!.StartsWith("Microsoft.AspNetCore")||x.Name!.Contains("Dapper")||x.Name!.Contains("MySql"));}
}
