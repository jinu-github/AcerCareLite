namespace AcerCareLite.Core.Acer;

/// <summary>
/// Whether this build contains write support at all. It is off in every default build, default install and every Release
/// publish unless <c>-p:AcerWriteEnabled=true</c> is passed on purpose (the install script does that only for -EnableWrite).
/// </summary>
public static class WriteBuild
{
    public static bool Compiled
    {
        get
        {
#if ACER_WRITE_ENABLED
            return true;
#else
            return false;
#endif
        }
    }
}
