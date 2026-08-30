# Integración con Aplicación Android

## Configuración Inicial en Android Studio

### 1. Añadir dependencias en `build.gradle` (Módulo: app)

```gradle
dependencies {
    // Retrofit para HTTP
    implementation 'com.squareup.retrofit2:retrofit:2.9.0'
    implementation 'com.squareup.retrofit2:converter-gson:2.9.0'

    // Gson para JSON
    implementation 'com.google.code.gson:gson:2.10.1'

    // OkHttp para logging
    implementation 'com.squareup.okhttp3:okhttp:4.11.0'
    implementation 'com.squareup.okhttp3:logging-interceptor:4.11.0'

    // Coroutines
    implementation 'org.jetbrains.kotlinx:kotlinx-coroutines-android:1.7.3'

    // SharedPreferences
    implementation 'androidx.security:security-crypto:1.1.0-alpha06'
}
```

## Crear Modelos en Kotlin

### AuthModels.kt
```kotlin
package com.example.servidorriego.models

data class LoginRequest(
    val username: String,
    val password: String
)

data class RegisterRequest(
    val username: String,
    val email: String,
    val password: String
)

data class LoginResponse(
    val success: Boolean,
    val message: String,
    val token: String?,
    val user: UserDto?
)

data class UserDto(
    val id: Int,
    val username: String,
    val email: String,
    val createdAt: String
)

data class RegisterResponse(
    val success: Boolean,
    val message: String,
    val user: UserDto?
)
```

## Crear API Interface

### RiegoApiService.kt
```kotlin
package com.example.servidorriego.api

import com.example.servidorriego.models.*
import retrofit2.http.*

interface RiegoApiService {

    @POST("auth/register")
    suspend fun register(@Body request: RegisterRequest): RegisterResponse

    @POST("auth/login")
    suspend fun login(@Body request: LoginRequest): LoginResponse

    @GET("auth/profile")
    suspend fun getProfile(@Header("Authorization") token: String): UserDto
}
```

## Crear Repository para Gestionar API

### AuthRepository.kt
```kotlin
package com.example.servidorriego.repository

import com.example.servidorriego.api.RiegoApiService
import com.example.servidorriego.models.*
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

class AuthRepository(private val apiService: RiegoApiService) {

    suspend fun register(username: String, email: String, password: String): Result<UserDto> {
        return withContext(Dispatchers.IO) {
            try {
                val request = RegisterRequest(username, email, password)
                val response = apiService.register(request)

                if (response.success && response.user != null) {
                    Result.success(response.user)
                } else {
                    Result.failure(Exception(response.message))
                }
            } catch (e: Exception) {
                Result.failure(e)
            }
        }
    }

    suspend fun login(username: String, password: String): Result<Pair<String, UserDto>> {
        return withContext(Dispatchers.IO) {
            try {
                val request = LoginRequest(username, password)
                val response = apiService.login(request)

                if (response.success && response.token != null && response.user != null) {
                    Result.success(Pair(response.token, response.user))
                } else {
                    Result.failure(Exception(response.message))
                }
            } catch (e: Exception) {
                Result.failure(e)
            }
        }
    }

    suspend fun getProfile(token: String): Result<UserDto> {
        return withContext(Dispatchers.IO) {
            try {
                val user = apiService.getProfile("Bearer $token")
                Result.success(user)
            } catch (e: Exception) {
                Result.failure(e)
            }
        }
    }
}
```

## Gestionar Token con SharedPreferences

### TokenManager.kt
```kotlin
package com.example.servidorriego.utils

import android.content.Context
import android.content.SharedPreferences
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey

class TokenManager(context: Context) {

    private val masterKey = MasterKey.Builder(context)
        .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
        .build()

    private val encryptedSharedPreferences: SharedPreferences =
        EncryptedSharedPreferences.create(
            context,
            "riego_prefs",
            masterKey,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
        )

    fun saveToken(token: String) {
        encryptedSharedPreferences.edit().putString("auth_token", token).apply()
    }

    fun getToken(): String? {
        return encryptedSharedPreferences.getString("auth_token", null)
    }

    fun clearToken() {
        encryptedSharedPreferences.edit().remove("auth_token").apply()
    }

    fun isLoggedIn(): Boolean {
        return getToken() != null
    }
}
```

## Crear Retrofit Client

### RetrofitClient.kt
```kotlin
package com.example.servidorriego.api

import com.example.servidorriego.BuildConfig
import okhttp3.OkHttpClient
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Retrofit
import retrofit2.converter.gson.GsonConverterFactory
import java.util.concurrent.TimeUnit

object RetrofitClient {

    private const val BASE_URL = "https://192.168.1.100:5001/api/"  // Cambiar por IP/dominio real

    fun getInstance(): RiegoApiService {
        val loggingInterceptor = HttpLoggingInterceptor().apply {
            level = if (BuildConfig.DEBUG) 
                HttpLoggingInterceptor.Level.BODY 
            else 
                HttpLoggingInterceptor.Level.NONE
        }

        val okHttpClient = OkHttpClient.Builder()
            .addInterceptor(loggingInterceptor)
            .connectTimeout(30, TimeUnit.SECONDS)
            .readTimeout(30, TimeUnit.SECONDS)
            .writeTimeout(30, TimeUnit.SECONDS)
            .build()

        return Retrofit.Builder()
            .baseUrl(BASE_URL)
            .client(okHttpClient)
            .addConverterFactory(GsonConverterFactory.create())
            .build()
            .create(RiegoApiService::class.java)
    }
}
```

## ViewModel para Autenticación

### AuthViewModel.kt
```kotlin
package com.example.servidorriego.viewmodel

import androidx.lifecycle.LiveData
import androidx.lifecycle.MutableLiveData
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.example.servidorriego.models.UserDto
import com.example.servidorriego.repository.AuthRepository
import com.example.servidorriego.utils.TokenManager
import kotlinx.coroutines.launch

class AuthViewModel(
    private val repository: AuthRepository,
    private val tokenManager: TokenManager
) : ViewModel() {

    private val _loginState = MutableLiveData<LoginState>()
    val loginState: LiveData<LoginState> = _loginState

    private val _registerState = MutableLiveData<RegisterState>()
    val registerState: LiveData<RegisterState> = _registerState

    private val _profileState = MutableLiveData<ProfileState>()
    val profileState: LiveData<ProfileState> = _profileState

    fun login(username: String, password: String) {
        viewModelScope.launch {
            _loginState.value = LoginState.Loading

            repository.login(username, password).onSuccess { (token, user) ->
                tokenManager.saveToken(token)
                _loginState.value = LoginState.Success(user)
            }.onFailure { error ->
                _loginState.value = LoginState.Error(error.message ?: "Error desconocido")
            }
        }
    }

    fun register(username: String, email: String, password: String) {
        viewModelScope.launch {
            _registerState.value = RegisterState.Loading

            repository.register(username, email, password).onSuccess { user ->
                _registerState.value = RegisterState.Success(user)
            }.onFailure { error ->
                _registerState.value = RegisterState.Error(error.message ?: "Error desconocido")
            }
        }
    }

    fun getProfile() {
        viewModelScope.launch {
            _profileState.value = ProfileState.Loading

            val token = tokenManager.getToken()
            if (token == null) {
                _profileState.value = ProfileState.Error("No hay token disponible")
                return@launch
            }

            repository.getProfile(token).onSuccess { user ->
                _profileState.value = ProfileState.Success(user)
            }.onFailure { error ->
                _profileState.value = ProfileState.Error(error.message ?: "Error desconocido")
            }
        }
    }

    fun logout() {
        tokenManager.clearToken()
        _loginState.value = LoginState.Idle
    }
}

// Estados
sealed class LoginState {
    object Idle : LoginState()
    object Loading : LoginState()
    data class Success(val user: UserDto) : LoginState()
    data class Error(val message: String) : LoginState()
}

sealed class RegisterState {
    object Idle : RegisterState()
    object Loading : RegisterState()
    data class Success(val user: UserDto) : RegisterState()
    data class Error(val message: String) : RegisterState()
}

sealed class ProfileState {
    object Idle : ProfileState()
    object Loading : ProfileState()
    data class Success(val user: UserDto) : ProfileState()
    data class Error(val message: String) : ProfileState()
}
```

## Ejemplo de Fragment para Login

### LoginFragment.kt
```kotlin
package com.example.servidorriego.ui

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.Toast
import androidx.fragment.app.Fragment
import androidx.fragment.app.viewModels
import com.example.servidorriego.api.RetrofitClient
import com.example.servidorriego.databinding.FragmentLoginBinding
import com.example.servidorriego.repository.AuthRepository
import com.example.servidorriego.utils.TokenManager
import com.example.servidorriego.viewmodel.AuthViewModel
import com.example.servidorriego.viewmodel.LoginState

class LoginFragment : Fragment() {

    private var _binding: FragmentLoginBinding? = null
    private val binding get() = _binding!!

    private val viewModel by viewModels<AuthViewModel> {
        val apiService = RetrofitClient.getInstance()
        val repository = AuthRepository(apiService)
        val tokenManager = TokenManager(requireContext())

        object : androidx.lifecycle.ViewModelProvider.Factory {
            override fun <T : androidx.lifecycle.ViewModel> create(modelClass: Class<T>): T {
                return AuthViewModel(repository, tokenManager) as T
            }
        }
    }

    override fun onCreateView(
        inflater: LayoutInflater,
        container: ViewGroup?,
        savedInstanceState: Bundle?
    ): View {
        _binding = FragmentLoginBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)

        setupListeners()
        setupObservers()
    }

    private fun setupListeners() {
        binding.btnLogin.setOnClickListener {
            val username = binding.etUsername.text.toString().trim()
            val password = binding.etPassword.text.toString()

            if (username.isEmpty() || password.isEmpty()) {
                Toast.makeText(requireContext(), "Completa todos los campos", Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }

            viewModel.login(username, password)
        }
    }

    private fun setupObservers() {
        viewModel.loginState.observe(viewLifecycleOwner) { state ->
            when (state) {
                is LoginState.Loading -> {
                    binding.btnLogin.isEnabled = false
                    Toast.makeText(requireContext(), "Iniciando sesión...", Toast.LENGTH_SHORT).show()
                }
                is LoginState.Success -> {
                    binding.btnLogin.isEnabled = true
                    Toast.makeText(
                        requireContext(),
                        "¡Bienvenido ${state.user.username}!",
                        Toast.LENGTH_SHORT
                    ).show()
                    // Navegar a pantalla principal
                }
                is LoginState.Error -> {
                    binding.btnLogin.isEnabled = true
                    Toast.makeText(requireContext(), state.message, Toast.LENGTH_LONG).show()
                }
                else -> {}
            }
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
```

## Configuración de AndroidManifest.xml

```xml
<!-- Permisos requeridos -->
<uses-permission android:name="android.permission.INTERNET" />
<uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />
```

## SSL/TLS en Android

Para desarrollo local con certificado autofirmado:

### network_security_config.xml
```xml
<?xml version="1.0" encoding="utf-8"?>
<network-security-config>
    <domain-config cleartextTrafficPermitted="false">
        <domain includeSubdomains="true">192.168.1.100</domain>
        <trust-anchors>
            <certificates src="@raw/selfsigned" />
        </trust-anchors>
    </domain-config>
</network-security-config>
```

En AndroidManifest.xml:
```xml
<application
    android:networkSecurityConfig="@xml/network_security_config"
    ...>
</application>
```

## Notas Importantes para Android

1. **URL Base**: Cambiar `192.168.1.100` por tu IP o dominio real
2. **Certificados**: Para producción, usar certificados válidos
3. **Token**: Se guarda encriptado en SharedPreferences
4. **Coroutinas**: Todas las llamadas de red se ejecutan en thread de IO
5. **Manejo de errores**: Implementar reintentos y fallbacks
6. **Timeout**: Configurado en 30 segundos

¡Tu aplicación Android ahora está lista para conectarse al servidor!
