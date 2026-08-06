# What an application's own project sees

Each line below was compiled into an assembly named `SomeoneElses.Bot`, which the
packages do not grant internals access to - so this is a consumer's compiler output.

## Removed or internal: consumer code no longer compiles

```text
> _ = services.AddTelegramRouter();
  CS1061: 'IServiceCollection' does not contain a definition for 'AddTelegramRouter' and no accessible extension method 'AddTelegramRouter' accepting a first argument of type 'IServiceCollection' could be found (are you missing a using directive or an assembly reference?)
> _ = services.AddTelegramFlow();
  CS1061: 'IServiceCollection' does not contain a definition for 'AddTelegramFlow' and no accessible extension method 'AddTelegramFlow' accepting a first argument of type 'IServiceCollection' could be found (are you missing a using directive or an assembly reference?)
> _ = services.AddVexelUpdateContexts();
  CS1061: 'IServiceCollection' does not contain a definition for 'AddVexelUpdateContexts' and no accessible extension method 'AddVexelUpdateContexts' accepting a first argument of type 'IServiceCollection' could be found (are you missing a using directive or an assembly reference?)
> _ = typeof(IUpdateDispatcher);
  CS0246: The type or namespace name 'IUpdateDispatcher' could not be found (are you missing a using directive or an assembly reference?)
> _ = typeof(CallbackAnswerObligation);
  CS0246: The type or namespace name 'CallbackAnswerObligation' could not be found (are you missing a using directive or an assembly reference?)
> _ = typeof(InlineAnswerObligation);
  CS0246: The type or namespace name 'InlineAnswerObligation' could not be found (are you missing a using directive or an assembly reference?)
> _ = typeof(AnswerObligation);
  CS0122: 'AnswerObligation' is inaccessible due to its protection level
> _ = typeof(CommandRouteMetadata);
  CS0246: The type or namespace name 'CommandRouteMetadata' could not be found (are you missing a using directive or an assembly reference?)
> _ = typeof(BotCommandRegistration);
  CS0246: The type or namespace name 'BotCommandRegistration' could not be found (are you missing a using directive or an assembly reference?)
> _ = typeof(RawUpdateHandlerRegistry);
  CS0122: 'RawUpdateHandlerRegistry' is inaccessible due to its protection level
> _ = typeof(CommandKeyExtractor);
  CS0122: 'CommandKeyExtractor' is inaccessible due to its protection level
> _ = typeof(CallbackKeyExtractor);
  CS0122: 'CallbackKeyExtractor' is inaccessible due to its protection level
> _ = typeof(InlineQueryKeyExtractor);
  CS0122: 'InlineQueryKeyExtractor' is inaccessible due to its protection level
> IHostBuilder builder = null!; _ = builder.AddTelegramService(static _ => "t");
  CS1929: 'IHostBuilder' does not contain a definition for 'AddTelegramService' and the best extension method overload 'ServiceCollectionExtensions.AddTelegramService(IServiceCollection, Func<IServiceProvider, string>, Action<VexelClientOptions>?)' requires a receiver of type 'Microsoft.Extensions.DependencyInjection.IServiceCollection'
```

## Supported wiring: compiles clean

```csharp
services.AddTelegramBot(_ => token);          // 0 errors
services.AddRawUpdateHandler<MyRawHandler>(); // 0 errors
```

