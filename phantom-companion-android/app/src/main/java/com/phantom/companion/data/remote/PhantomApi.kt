package com.phantom.companion.data.remote

import com.phantom.companion.domain.model.AuthSession
import com.phantom.companion.domain.model.CompanionSessionSnapshot
import com.phantom.companion.domain.model.ForgotPasswordRequest
import com.phantom.companion.domain.model.HealthResponse
import com.phantom.companion.domain.model.LoginRequest
import com.phantom.companion.domain.model.LogoutRequest
import com.phantom.companion.domain.model.MessageResponse
import com.phantom.companion.domain.model.Pairing
import com.phantom.companion.domain.model.PairingCompleteRequest
import com.phantom.companion.domain.model.PairingsResponse
import com.phantom.companion.domain.model.RefreshRequest
import com.phantom.companion.domain.model.RelayTicket
import com.phantom.companion.domain.model.RelayTicketRequest
import com.phantom.companion.domain.model.RevokedResponse
import com.phantom.companion.domain.model.StartupSnapshot
import retrofit2.Response
import retrofit2.http.Body
import retrofit2.http.DELETE
import retrofit2.http.GET
import retrofit2.http.POST
import retrofit2.http.Path

interface PhantomApi {

    @GET("health")
    suspend fun health(): HealthResponse

    @POST("api/desktop/auth/login")
    suspend fun login(@Body body: LoginRequest): AuthSession

    @POST("api/desktop/auth/refresh")
    suspend fun refresh(@Body body: RefreshRequest): AuthSession

    @POST("api/desktop/auth/logout")
    suspend fun logout(@Body body: LogoutRequest): RevokedResponse

    @GET("api/desktop/auth/me")
    suspend fun me(): AuthSession

    @POST("api/desktop/account/startup-check/session")
    suspend fun startupCheck(@Body session: AuthSession): StartupSnapshot

    @POST("api/desktop/auth/forgot-password")
    suspend fun forgotPassword(@Body body: ForgotPasswordRequest): MessageResponse

    @GET("api/companion/pairings")
    suspend fun pairings(): PairingsResponse

    // Raw response version for probe checking 404 vs 200 without throwing
    @GET("api/companion/pairings")
    suspend fun pairingsRaw(): Response<PairingsResponse>

    @POST("api/companion/pairings/complete")
    suspend fun completePairing(@Body body: PairingCompleteRequest): Pairing

    @DELETE("api/companion/pairings/{pairingId}")
    suspend fun revokePairing(@Path("pairingId") pairingId: String): RevokedResponse

    @POST("api/companion/relay-ticket")
    suspend fun relayTicket(@Body body: RelayTicketRequest): RelayTicket

    @GET("api/companion/sessions/current")
    suspend fun currentSession(): CompanionSessionSnapshot

    @GET("api/companion/sessions/current")
    suspend fun currentSessionRaw(): Response<CompanionSessionSnapshot>
}
