﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Tgstation.Server.Api;
using Tgstation.Server.Api.Models;
using Tgstation.Server.Api.Rights;
using Tgstation.Server.Host.Components;
using Tgstation.Server.Host.Database;
using Tgstation.Server.Host.Jobs;
using Tgstation.Server.Host.Models;
using Tgstation.Server.Host.Security;

namespace Tgstation.Server.Host.Controllers
{
	/// <summary>
	/// <see cref="ApiController"/> for managing the deployment system.
	/// </summary>
	[Route(Routes.DreamMaker)]
	#pragma warning disable CA1506 // TODO: Decomplexify
	public sealed class DreamMakerController : ApiController
	{
		/// <summary>
		/// The <see cref="IJobManager"/> for the <see cref="DreamMakerController"/>
		/// </summary>
		readonly IJobManager jobManager;

		/// <summary>
		/// The <see cref="IInstanceManager"/> for the <see cref="DreamMakerController"/>
		/// </summary>
		readonly IInstanceManager instanceManager;

		/// <summary>
		/// Construct a <see cref="DreamMakerController"/>
		/// </summary>
		/// <param name="databaseContext">The <see cref="IDatabaseContext"/> for the <see cref="ApiController"/></param>
		/// <param name="authenticationContextFactory">The <see cref="IAuthenticationContextFactory"/> for the <see cref="ApiController"/></param>
		/// <param name="jobManager">The value of <see cref="jobManager"/></param>
		/// <param name="instanceManager">The value of <see cref="instanceManager"/></param>
		/// <param name="logger">The <see cref="ILogger"/> for the <see cref="ApiController"/></param>
		public DreamMakerController(IDatabaseContext databaseContext, IAuthenticationContextFactory authenticationContextFactory, IJobManager jobManager, IInstanceManager instanceManager, ILogger<DreamMakerController> logger) : base(databaseContext, authenticationContextFactory, logger, true, true)
		{
			this.jobManager = jobManager ?? throw new ArgumentNullException(nameof(jobManager));
			this.instanceManager = instanceManager ?? throw new ArgumentNullException(nameof(instanceManager));
		}

		/// <summary>
		/// Read current <see cref="DreamMaker"/> status.
		/// </summary>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="IActionResult"/> of the request.</returns>
		/// <response code="200">Read <see cref="DreamMaker"/> status successfully.</response>
		[HttpGet]
		[TgsAuthorize(DreamMakerRights.Read)]
		[ProducesResponseType(typeof(DreamMaker), 200)]
		public async Task<IActionResult> Read(CancellationToken cancellationToken)
		{
			var instance = instanceManager.GetInstance(Instance);
			var dreamMakerSettings = await DatabaseContext.DreamMakerSettings.Where(x => x.InstanceId == Instance.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
			return Json(dreamMakerSettings.ToApi());
		}

		/// <summary>
		/// Get a <see cref="Api.Models.CompileJob"/> specified by a given <paramref name="id"/>.
		/// </summary>
		/// <param name="id">The <see cref="EntityId.Id"/>.</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="IActionResult"/> of the request.</returns>
		/// <response code="200"><see cref="Api.Models.CompileJob"/> retrieved successfully.</response>
		/// <response code="404">Specified compile job does not exist in this instance.</response>
		[HttpGet("{id}")]
		[TgsAuthorize(DreamMakerRights.CompileJobs)]
		[ProducesResponseType(typeof(Api.Models.CompileJob), 200)]
		[ProducesResponseType(404)]
		public async Task<IActionResult> GetId(long id, CancellationToken cancellationToken)
		{
			var compileJob = await DatabaseContext.CompileJobs
				.Where(x => x.Id == id && x.Job.Instance.Id == Instance.Id)
				.Include(x => x.Job).ThenInclude(x => x.StartedBy)
				.Include(x => x.RevisionInformation).ThenInclude(x => x.PrimaryTestMerge).ThenInclude(x => x.MergedBy)
				.Include(x => x.RevisionInformation).ThenInclude(x => x.ActiveTestMerges).ThenInclude(x => x.TestMerge).ThenInclude(x => x.MergedBy)
				.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
			if (compileJob == default)
				return NotFound();
			return Json(compileJob.ToApi());
		}

		/// <summary>
		/// List all <see cref="Api.Models.CompileJob"/> <see cref="EntityId"/>s for the instance.
		/// </summary>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="IActionResult"/> of the request.</returns>
		/// <response code="200">Retrieved <see cref="EntityId"/>s successfully.</response>
		[HttpGet(Routes.List)]
		[TgsAuthorize(DreamMakerRights.CompileJobs)]
		[ProducesResponseType(typeof(List<EntityId>), 200)]
		public async Task<IActionResult> List(CancellationToken cancellationToken)
		{
			var compileJobs = await DatabaseContext.CompileJobs.Where(x => x.Job.Instance.Id == Instance.Id).OrderByDescending(x => x.Job.StoppedAt).Select(x => new EntityId
			{
				Id = x.Id
			}).ToListAsync(cancellationToken).ConfigureAwait(false);
			return Json(compileJobs);
		}

		/// <summary>
		/// Begin deploying repository code.
		/// </summary>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="IActionResult"/> of the request.</returns>
		/// <response code="202">Created deployment <see cref="Api.Models.Job"/> successfully.</response>
		[HttpPut]
		[TgsAuthorize(DreamMakerRights.Compile)]
		[ProducesResponseType(typeof(Api.Models.Job), 202)]
		public async Task<IActionResult> Create(CancellationToken cancellationToken)
		{
			var job = new Models.Job
			{
				Description = "Compile active repository code",
				StartedBy = AuthenticationContext.User,
				CancelRightsType = RightsType.DreamMaker,
				CancelRight = (ulong)DreamMakerRights.CancelCompile,
				Instance = Instance
			};
			await jobManager.RegisterOperation(job, instanceManager.GetInstance(Instance).CompileProcess, cancellationToken).ConfigureAwait(false);
			return Accepted(job.ToApi());
		}

		/// <summary>
		/// Update deployment settings.
		/// </summary>
		/// <param name="model">The updated <see cref="DreamMaker"/> settings.</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="IActionResult"/> of the request.</returns>
		/// <response code="200">Changes applied successfully. The updated <see cref="DreamMaker"/> settings will be returned based on user permissions.</response>
		/// <response code="410">Instance no longer available.</response>
		[HttpPost]
		[TgsAuthorize(DreamMakerRights.SetDme | DreamMakerRights.SetApiValidationPort | DreamMakerRights.SetApiValidationPort)]
		[ProducesResponseType(typeof(DreamMaker), 200)]
		[ProducesResponseType(200)]
		[ProducesResponseType(410)]
		public async Task<IActionResult> Update([FromBody] DreamMaker model, CancellationToken cancellationToken)
		{
			if (model == null)
				throw new ArgumentNullException(nameof(model));

			if (model.ApiValidationPort == 0)
				return BadRequest(new ErrorMessage { Message = "API Validation port cannot be 0!" });

			if (model.ApiValidationSecurityLevel == DreamDaemonSecurity.Ultrasafe)
				return BadRequest(new ErrorMessage { Message = "This version of TGS does not support the ultrasafe DreamDaemon configuration!" });

			var hostModel = await DatabaseContext.DreamMakerSettings.Where(x => x.InstanceId == Instance.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
			if (hostModel == null)
				return StatusCode((int)HttpStatusCode.Gone);

			if (model.ProjectName != null)
			{
				if (!AuthenticationContext.InstanceUser.DreamMakerRights.Value.HasFlag(DreamMakerRights.SetDme))
					return Forbid();
				if (model.ProjectName.Length == 0)
					hostModel.ProjectName = null;
				else
					hostModel.ProjectName = model.ProjectName;
			}

			if (model.ApiValidationPort.HasValue)
			{
				if (!AuthenticationContext.InstanceUser.DreamMakerRights.Value.HasFlag(DreamMakerRights.SetApiValidationPort))
					return Forbid();
				hostModel.ApiValidationPort = model.ApiValidationPort;
			}

			if (model.ApiValidationSecurityLevel.HasValue)
			{
				if (!AuthenticationContext.InstanceUser.DreamMakerRights.Value.HasFlag(DreamMakerRights.SetSecurityLevel))
					return Forbid();
				hostModel.ApiValidationSecurityLevel = model.ApiValidationSecurityLevel;
			}

			await DatabaseContext.Save(cancellationToken).ConfigureAwait(false);

			if ((AuthenticationContext.GetRight(RightsType.DreamMaker) & (ulong)DreamMakerRights.Read) == 0)
				return Ok();

			return await Read(cancellationToken).ConfigureAwait(false);
		}
	}
}
