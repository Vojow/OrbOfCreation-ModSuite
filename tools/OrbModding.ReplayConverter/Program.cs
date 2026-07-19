using OrbModding.ReplayConverter;

if (args.Length != 8 ||
    args[0] != "--setup" ||
    args[2] != "--observations" ||
    args[4] != "--output" ||
    args[6] != "--replay-id")
{
    Console.Error.WriteLine("Usage: OrbModding.ReplayConverter --setup <reviewed-setup.json> --observations <sanitized.jsonl> --output <fixture.json> --replay-id <id>");
    return 2;
}

try
{
    ReplayConversion.Convert(args[1], args[3], args[5], args[7]);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
