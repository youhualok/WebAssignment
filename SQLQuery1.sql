DELETE FROM dbo.Users
WHERE Username = 'guest';

SELECT UserId, Username, Email, Role, ManagedCinemaId
FROM dbo.Users;