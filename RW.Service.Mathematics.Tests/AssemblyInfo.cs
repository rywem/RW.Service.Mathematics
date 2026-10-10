// Several tests here assert that an operation finishes inside its ten-second budget. Run in
// parallel on a four-core machine they starve each other and fail at random (AtTheLimit_ItStillWorks
// failed about one run in three). Correctness tests do not need the cores; timing tests need them
// to themselves.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
