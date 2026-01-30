<template>
  <section class="card" v-if="results.length || error">
    <h2>Sistem Testi Sonuçları</h2>
    <div class="muted" v-if="ranAt">Çalıştırma zamanı: {{ ranAt }}</div>
    <div class="error" v-if="error">{{ error }}</div>
    <ul class="system-test-list" v-if="results.length">
      <li v-for="(check, idx) in results" :key="idx">
        <span
          :class="[
            'system-test-badge',
            check.success ? 'system-test-pass' : 'system-test-fail',
          ]"
        >
          {{ check.success ? 'PASS' : 'FAIL' }}
        </span>
        <div class="system-test-body">
          <div class="system-test-name">{{ check.name }}</div>
          <div class="muted">{{ check.message }}</div>
        </div>
      </li>
    </ul>
  </section>
</template>

<script setup>
defineProps({
  results: {
    type: Array,
    required: true,
  },
  error: {
    type: String,
    default: "",
  },
  ranAt: {
    type: String,
    default: "",
  },
});
</script>
