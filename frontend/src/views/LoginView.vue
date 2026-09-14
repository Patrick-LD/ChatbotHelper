<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import BaseButton from '@/components/BaseButton.vue'
import { ApiError } from '@/api/errors'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()

const key = ref('')
const error = ref<ApiError | null>(null)
const busy = ref(false)

/** Udviklingsgenveje til de tre nøgler i appsettings.Development.json. Kompileres væk i produktion. */
const devKeys = import.meta.env.DEV
  ? [
      { label: 'Anna (medarbejder)', key: 'dev-medarbejder-2026-noegle' },
      { label: 'Hanne (HR)', key: 'dev-hr-2026-noegle' },
      { label: 'Adam (admin)', key: 'dev-admin-2026-noegle' },
    ]
  : []

async function submit(value = key.value) {
  if (busy.value) return
  error.value = null
  busy.value = true
  try {
    await auth.login(value)
    const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/'
    await router.replace(redirect)
  } catch (e) {
    error.value = e instanceof ApiError ? e : new ApiError(0, 'Kunne ikke logge ind')
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="login container">
    <section class="login__card card" aria-labelledby="login-title">
      <h1 id="login-title" class="display login__title">Log ind</h1>
      <p class="login__lead">Indtast din API-nøgle for at bruge IST Assistent.</p>

      <form class="login__form" @submit.prevent="submit()">
        <label for="api-key" class="login__label">API-nøgle</label>
        <input
          id="api-key"
          v-model="key"
          class="login__input"
          type="password"
          autocomplete="current-password"
          spellcheck="false"
          required
          :aria-invalid="error ? 'true' : undefined"
          :aria-describedby="error ? 'login-error' : undefined"
        />
        <p v-if="error" id="login-error" class="login__error" role="alert">
          <strong>{{ error.title }}</strong>
          <span v-if="error.detail"> {{ error.detail }}</span>
        </p>
        <BaseButton type="submit" size="lg" :loading="busy" :disabled="!key.trim()">Log ind</BaseButton>
      </form>

      <div v-if="devKeys.length" class="login__dev">
        <p class="login__dev-title">Udvikling — log ind som</p>
        <div class="login__dev-buttons">
          <BaseButton
            v-for="dev in devKeys"
            :key="dev.key"
            variant="outline"
            :disabled="busy"
            @click="submit(dev.key)"
          >
            {{ dev.label }}
          </BaseButton>
        </div>
      </div>
    </section>
  </div>
</template>

<style scoped>
.login {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  padding-block: 40px;
}

.login__card {
  width: 100%;
  max-width: 480px;
  padding: 40px 36px;
}

.login__title {
  font-size: 40px;
  margin-bottom: 8px;
}

.login__lead {
  color: var(--color-muted);
  margin-bottom: 24px;
}

.login__form {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.login__label {
  font-weight: 700;
  font-size: 14px;
}

.login__input {
  padding: 14px 18px;
  border: 2px solid var(--color-border);
  border-radius: var(--radius-pill);
  font-size: 16px;
}

.login__input:focus {
  border-color: var(--color-primary);
  box-shadow: 0 0 0 0.25rem rgba(201, 246, 220, 0.4);
  outline: 0;
}

.login__error {
  margin: 0;
  padding: 10px 14px;
  border-radius: var(--radius-sm);
  background: var(--color-secondary-3);
  font-size: 14px;
}

.login__dev {
  margin-top: 28px;
  padding-top: 20px;
  border-top: 1px dashed var(--color-border);
}

.login__dev-title {
  margin: 0 0 10px;
  font-size: 13px;
  font-weight: 700;
  color: var(--color-muted);
  text-transform: uppercase;
  letter-spacing: 0.06em;
}

.login__dev-buttons {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
</style>
