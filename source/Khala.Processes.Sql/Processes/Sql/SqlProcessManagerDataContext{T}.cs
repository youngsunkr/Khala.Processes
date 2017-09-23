﻿namespace Khala.Processes.Sql
{
    using System;
    using System.Collections.Generic;
    using System.Data.Entity;
    using System.Diagnostics;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Khala.Messaging;

    public sealed class SqlProcessManagerDataContext<T> : IDisposable
        where T : ProcessManager
    {
        private readonly IProcessManagerDbContext<T> _dbContext;
        private readonly IMessageSerializer _serializer;
        private readonly ICommandPublisher _commandPublisher;
        private readonly ICommandPublisherExceptionHandler _commandPublisherExceptionHandler;

        public SqlProcessManagerDataContext(
            IProcessManagerDbContext<T> dbContext,
            IMessageSerializer serializer,
            ICommandPublisher commandPublisher,
            ICommandPublisherExceptionHandler commandPublisherExceptionHandler)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _commandPublisher = commandPublisher ?? throw new ArgumentNullException(nameof(commandPublisher));
            _commandPublisherExceptionHandler = commandPublisherExceptionHandler ?? throw new ArgumentNullException(nameof(commandPublisherExceptionHandler));
        }

        public SqlProcessManagerDataContext(
            IProcessManagerDbContext<T> dbContext,
            IMessageSerializer serializer,
            ICommandPublisher commandPublisher)
            : this(
                dbContext,
                serializer,
                commandPublisher,
                DefaultCommandPublisherExceptionHandler.Instance)
        {
        }

        public void Dispose() => _dbContext.Dispose();

        public Task<T> Find(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            async Task<T> Run()
            {
                T processManager = await FindSingle(predicate, cancellationToken).ConfigureAwait(false);
                await FlushCommandsIfProcessManagerExists(processManager, cancellationToken).ConfigureAwait(false);
                return processManager;
            }

            return Run();
        }

        private Task<T> FindSingle(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken)
            => _dbContext
            .ProcessManagers
            .Where(predicate)
            .SingleOrDefaultAsync(cancellationToken);

        private Task FlushCommandsIfProcessManagerExists(T processManager, CancellationToken cancellationToken)
            => processManager == null
            ? Task.FromResult(true)
            : _commandPublisher.FlushCommands(processManager.Id, cancellationToken);

        public Task SaveProcessManagerAndPublishCommands(
            T processManager,
            Guid? correlationId,
            CancellationToken cancellationToken)
        {
            if (processManager == null)
            {
                throw new ArgumentNullException(nameof(processManager));
            }

            async Task Run()
            {
                await SaveProcessManagerAndCommands(processManager, correlationId, cancellationToken).ConfigureAwait(false);
                await FlushCommands(processManager, cancellationToken).ConfigureAwait(false);
            }

            return Run();
        }

        private Task SaveProcessManagerAndCommands(
            T processManager,
            Guid? correlationId,
            CancellationToken cancellationToken)
        {
            UpsertProcessManager(processManager);
            InsertPendingCommands(processManager, correlationId);
            return Commit(cancellationToken);
        }

        private void UpsertProcessManager(T processManager)
        {
            if (_dbContext.Entry(processManager).State == EntityState.Detached)
            {
                _dbContext.ProcessManagers.Add(processManager);
            }
        }

        private void InsertPendingCommands(T processManager, Guid? correlationId)
        {
            IEnumerable<PendingCommand> pendingCommands = processManager
                .FlushPendingCommands()
                .Select(command => new Envelope(Guid.NewGuid(), correlationId, command))
                .Select(envelope => PendingCommand.FromEnvelope(processManager, envelope, _serializer));

            _dbContext.PendingCommands.AddRange(pendingCommands);
        }

        private Task Commit(CancellationToken cancellationToken)
        {
            return _dbContext.SaveChangesAsync(cancellationToken);
        }

        private async Task FlushCommands(
            T processManager,
            CancellationToken cancellationToken)
        {
            try
            {
                await _commandPublisher.FlushCommands(processManager.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                var context = new CommandPublisherExceptionContext(typeof(T), processManager.Id, exception);
                try
                {
                    await _commandPublisherExceptionHandler.Handle(context).ConfigureAwait(false);
                }
                catch (Exception unhandleable)
                {
                    Trace.TraceError(unhandleable.ToString());
                }

                if (context.Handled == false)
                {
                    throw;
                }
            }
        }
    }
}
