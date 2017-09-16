﻿namespace Khala.Processes
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Provides functions to publish commands generated by process managers.
    /// </summary>
    public interface ICommandPublisher
    {
        /// <summary>
        /// Flush pending commands generated by specified process manager.
        /// </summary>
        /// <param name="processManagerId">The identifier of the process manager having commands to be sent.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for the task to complete.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task FlushCommands(Guid processManagerId, CancellationToken cancellationToken);

        /// <summary>
        /// Starts to publish pending commands for all process managers having commands not published yet.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for the task to complete.</param>
        void EnqueueAll(CancellationToken cancellationToken);
    }
}
