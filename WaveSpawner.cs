using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class WaveSpawner : MonoBehaviour
{
    public static int EnemiesAlive = 0;
    public Wave[] waves;
    public Transform spawnPoint;
    private float countdown = 100f;
    public TextMeshProUGUI waveCountdownText;
    public TextMeshProUGUI activeWaves;
    public string activeWavesString;
    public GameManager gameManager;
    private int waveIndex;
    private List<Coroutine> activeCoroutines = new List<Coroutine>();

    void Start()
    {
        EnemiesAlive = 0;
        waveIndex = gameManager.reverseMode ? waves.Length - 1 : 0;
    }

    void Update()
    {
        activeWavesString = activeWaves.text;

        if (waveIndex >= waves.Length && !AnyEnemiesAlive() && activeWaves.text.Length < 15)
        {
            gameManager.WinLevel();
            enabled = false;
            return;
        }

        if (countdown <= 0f && waveIndex != waves.Length)
            SendNextWave();

        countdown -= Time.deltaTime;
        countdown = Mathf.Clamp(countdown, 0f, Mathf.Infinity);
        waveCountdownText.text = "Next Wave in: " + string.Format("{0:00}", countdown);
    }

    IEnumerator SpawnWave(Wave wave)
    {
        PlayerStats.Rounds++;
        int waveInd = waveIndex + 1;
        string waveString = waveInd + "";
        activeWaves.text = activeWaves.text + waveString + " ";

        for (int z = 0; z < wave.enemies.Length; z++)
        {
            for (int i = 0; i < wave.enemies[z].count; i++)
            {
                SpawnEnemy(wave.enemies[z].enemy);
                EnemiesAlive++;
                yield return new WaitForSeconds(1f / wave.spawnRate);
            }
        }

        int index = activeWaves.text.IndexOf(waveString + " ");
        if (index >= 0)
            activeWaves.text = activeWaves.text.Remove(index, (waveString + " ").Length);
    }

    void SpawnEnemy(GameObject enemy)
    {
        float randomNumber = Random.Range(-1f, 1f);
        Instantiate(enemy, spawnPoint.position, spawnPoint.rotation);
    }

    public void SendNextWave()
    {
        if (waveIndex != waves.Length || waveIndex < 1)
        {
            Wave wave = waves[waveIndex];
            Coroutine coroutine = StartCoroutine(SpawnWave(wave));
            waveIndex += gameManager.reverseMode ? -1 : 1;
            activeCoroutines.Add(coroutine);
            countdown = wave.nextWaveDelay;
        }
    }

    private bool AnyEnemiesAlive()
    {
        var gos = GameObject.FindGameObjectsWithTag("Enemy");
        for (int i = 0; i < gos.Length; i++)
        {
            var go = gos[i];
            if (!go || !go.activeInHierarchy) continue;

            if (go.TryGetComponent<Enemy>(out var e))
            {
                if (!e.isDead) return true;
            }
            else
            {
                // Tagged "Enemy" but no Enemy component — treat as alive to avoid a false win.
                return true;
            }
        }
        return false;
    }
}
