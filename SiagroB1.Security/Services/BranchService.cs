using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Dtos;

namespace SiagroB1.Security.Services;

public class BranchService(
    CommonDbContext commonCtx,
    AppDbContext appCtx
    )
{
    public async Task SetDefaultBranch(string sessionId, string branchCode)
    {
        var userSession = await commonCtx.UserSessions.FirstOrDefaultAsync(x => x.SessionId == sessionId);
        if (userSession == null)
            throw new NotFoundException("Session not found");
        
        userSession.BranchCode = branchCode;
        
        commonCtx.Entry(userSession).State = EntityState.Modified;
        
        await commonCtx.SaveChangesAsync();
    }

    public async Task<BranchInfo> GetDefaultBranchInfo(string sessionId)
    {
        var userSession = await commonCtx.UserSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.SessionId == sessionId);
        
        if (userSession == null)
            throw new NotFoundException("Session not found");

        var branchCode = userSession.BranchCode;

        var branch = await appCtx.Branchs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Code == branchCode);
        
        return new BranchInfo()
        {
            Code = branch?.Code,
            BranchName = branch?.BranchName,
            ShortName = branch?.ShortName,
            TaxId =  branch?.TaxId,
        };
    }
}