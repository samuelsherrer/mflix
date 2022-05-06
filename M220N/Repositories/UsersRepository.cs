﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using M220N.Models;
using M220N.Models.Responses;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;

namespace M220N.Repositories
{
    public class UsersRepository
    {
        private readonly IMongoCollection<Session> _sessionsCollection;
        private readonly IMongoCollection<User> _usersCollection;

        public UsersRepository(IMongoClient mongoClient)
        {
            var camelCaseConvention = new ConventionPack {new CamelCaseElementNameConvention()};
            ConventionRegistry.Register("CamelCase", camelCaseConvention, type => true);

            _usersCollection = mongoClient.GetDatabase("sample_mflix").GetCollection<User>("users");
            _sessionsCollection = mongoClient.GetDatabase("sample_mflix").GetCollection<Session>("sessions");
        }

        /*
        Ticket: User Management

        For this ticket, you will need to implement the following five methods:

        - GetUserAsync
        - AddUserAsync
        - LoginUserAsync
        - LogoutUserAsync
        - GetUserSessionAsync

        You can find these methods below this comment. Make sure to read the comments
        in each method to better understand the implementation.

        The method DeleteUserAsync is already given to you. You can find it at
        the end of this class.
        */

        /// <summary>
        ///     Finds a user in the `users` collection
        /// </summary>
        /// <param name="email">The Email of the User</param>
        /// <param name="cancellationToken">Allows the UI to cancel an asynchronous request. Optional.</param>
        /// <returns>A User or null</returns>
        public async Task<User> GetUserAsync(string email, CancellationToken cancellationToken = default)
        {
            return await _usersCollection.Find(user => user.Email == email)
                .FirstOrDefaultAsync(cancellationToken);
        }

        /// <summary>
        ///     Adds a user to the `users` collection
        /// </summary>
        /// <param name="name">The name of the user.</param>
        /// <param name="email">The email of the user.</param>
        /// <param name="password">The clear-text password, which will be hashed before storing.</param>
        /// <param name="cancellationToken">Allows the UI to cancel an asynchronous request. Optional.</param>
        /// <returns></returns>
        public async Task<UserResponse> AddUserAsync(string name, string email, string password,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var passwordHash = PasswordHashOMatic.Hash(password);

                var user = new User()
                {
                    Name = name,
                    Email = email,
                    HashedPassword = passwordHash
                };

                await _usersCollection
                    .WithWriteConcern(WriteConcern.WMajority)
                    .InsertOneAsync(user, null, cancellationToken);

                var newUser = await GetUserAsync(user.Email, cancellationToken);
                return new UserResponse(newUser);
            }
            catch (Exception ex)
            {
                return ex.Message.StartsWith("MongoError: E11000 duplicate key error")
                    ? new UserResponse(false, "A user with the given email already exists.")
                    : new UserResponse(false, ex.Message);
            }
        }

        /// <summary>
        ///     Adds a user to the `sessions` collection
        /// </summary>
        /// <param name="user">The User to add.</param>
        /// <param name="cancellationToken">Allows the UI to cancel an asynchronous request. Optional.</param>
        /// <returns></returns> 
        public async Task<UserResponse> LoginUserAsync(User user, CancellationToken cancellationToken = default)
        {
            try
            {
                var storedUser = await GetUserAsync(user.Email, cancellationToken);
                if (storedUser == null)
                {
                    return new UserResponse(false, "No user found. Please check the email address.");
                }

                if (user.HashedPassword != null && user.HashedPassword != storedUser.HashedPassword)
                {
                    return new UserResponse(false, "The hashed password provided is not valid");
                }

                if (user.HashedPassword == null && !PasswordHashOMatic.Verify(user.Password, storedUser.HashedPassword))
                {
                    return new UserResponse(false, "The password provided is not valid");
                }

                var session = await _sessionsCollection.Find(s => s.UserId == user.Email)
                    .FirstOrDefaultAsync(cancellationToken);

                if (session is null) session = new Session();

                session.UserId = user.Email;
                session.Jwt = user.AuthToken;

                var options = new UpdateOptions {IsUpsert = true};
                var update = Builders<Session>.Update
                    .Set(p => p.UserId, user.Email)
                    .Set(p => p.Jwt, user.AuthToken);

                await _sessionsCollection.UpdateOneAsync(
                    s => s.UserId == user.Email,
                    update,
                    options,
                    cancellationToken);

                storedUser.AuthToken = user.AuthToken;
                return new UserResponse(storedUser);
            }
            catch (Exception ex)
            {
                return new UserResponse(false, ex.Message);
            }
        }

        /// <summary>
        ///     Removes a user from the `sessions` collection, which is the
        ///     equivalent of logging out.
        /// </summary>
        /// <param name="email">The Email of the User to log out.</param>
        /// <param name="cancellationToken">Allows the UI to cancel an asynchronous request. Optional.</param>
        /// <returns></returns>
        public async Task<UserResponse> LogoutUserAsync(string email, CancellationToken cancellationToken = default)
        {
            await _sessionsCollection.DeleteOneAsync(new BsonDocument("user_id", email), cancellationToken);
            return new UserResponse(true, "User logged out.");
        }

        /// <summary>
        ///     Gets a user from the `sessions` collection.
        /// </summary>
        /// <param name="email">The Email of the User to fetch.</param>
        /// <param name="cancellationToken">Allows the UI to cancel an asynchronous request. Optional.</param>
        /// <returns></returns>
        public async Task<Session> GetUserSessionAsync(string email, CancellationToken cancellationToken = default)
        {
            return await _sessionsCollection.Find(new BsonDocument("user_id", email)).FirstOrDefaultAsync();
        }

        /// <summary>
        ///     Removes a user from the `sessions` and `users` collections
        /// </summary>
        /// <param name="email">The Email of the User to delete.</param>
        /// <param name="cancellationToken">Allows the UI to cancel an asynchronous request. Optional.</param>
        /// <returns></returns>
        public async Task<UserResponse> DeleteUserAsync(string email, CancellationToken cancellationToken = default)
        {
            try
            {
                await _usersCollection.DeleteOneAsync(new BsonDocument("email", email), cancellationToken);
                await _sessionsCollection.DeleteOneAsync(new BsonDocument("user_id", email), cancellationToken);

                var deletedUser = await _usersCollection.FindAsync<User>(new BsonDocument("email", email),
                    cancellationToken: cancellationToken);
                var deletedSession = await _sessionsCollection.FindAsync<Session>(new BsonDocument("user_id", email),
                    cancellationToken: cancellationToken);
                if (deletedUser.FirstOrDefault() == null && deletedSession.FirstOrDefault() == null)
                    return new UserResponse(true, "User deleted");
                return new UserResponse(false, "User deletion was unsuccessful");
            }
            catch (Exception ex)
            {
                return new UserResponse(false, ex.ToString());
            }
        }

        /// <summary>
        ///     Given a user's email, and an object of new preferences, update that user's
        ///     data to include those preferences.
        /// </summary>
        /// <param name="email">The Email of the User to update.</param>
        /// <param name="preferences">The collection of preferences to set.</param>
        /// <param name="cancellationToken">Allows the UI to cancel an asynchronous request. Optional.</param>
        /// <returns></returns>
        public async Task<UserResponse> SetUserPreferencesAsync(string email,
            Dictionary<string, string> preferences, CancellationToken cancellationToken = default)
        {
            try
            {
                var updateResult = await _usersCollection.UpdateOneAsync(
                    e => e.Email == email,
                    Builders<User>.Update.Set("preferences", preferences),
                    new UpdateOptions() { IsUpsert = false },
                    cancellationToken);

                return updateResult.MatchedCount == 0
                    ? new UserResponse(false, "No user found with that email")
                    : new UserResponse(true, updateResult.IsAcknowledged.ToString());
            }
            catch (Exception e)
            {
                return new UserResponse(false, e.Message);
            }
        }

        public async Task<User> MakeAdmin(User user, CancellationToken cancellationToken = default)
        {
            user.IsAdmin = true;
            await _usersCollection.InsertOneAsync(user, null, cancellationToken);
            return await GetUserAsync(user.Email, cancellationToken);
        }
    }
}