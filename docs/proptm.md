im getting this error from the playground 

System.InvalidCastException: Unable to cast object of type 'System.Text.Json.JsonElement' to type 'System.IConvertible'.
   at System.Convert.ToDouble(Object value)
   at Fraud.Engine.Rules.MouseVelocityRule.<>c.<EvaluateAsync>b__4_1(Signal s)
   at System.Linq.Enumerable.ListSelectIterator`2.MoveNext()
   at System.Linq.Enumerable.IEnumerableWhereIterator`1.ToList()
   at System.Linq.Enumerable.ToList[TSource](IEnumerable`1 source)
   at Fraud.Engine.Rules.MouseVelocityRule.EvaluateAsync(IReadOnlyList`1 signals, CancellationToken cancellationToken)
   at Fraud.Engine.Rules.RuleEngine.EvaluateAsync(IReadOnlyList`1 signals, CancellationToken cancellationToken)
   at Fraud.Engine.FraudEvaluator.EvaluateAsync(Session session, IReadOnlyList`1 signals, CancellationToken cancellationToken)
   at Program.<>c.<<<Main>$>b__0_5>d.MoveNext()
--- End of stack trace from previous location ---
   at Microsoft.AspNetCore.Http.RequestDelegateFactory.ExecuteTaskResult[T](Task`1 task, HttpContext httpContext)
   at Microsoft.AspNetCore.Diagnostics.DeveloperExceptionPageMiddlewareImpl.Invoke(HttpContext context)

after anylisyng this please outup the options for the next steps agains so that we can decide how to move further 