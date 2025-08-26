param([Parameter(ValueFromRemainingArguments=$true)] $Args)
dotnet run --project Stroll.PrettyTest -- @Args
