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
        if(gameManager.reverseMode){
            waveIndex = waves.Length - 1;
        }
        else{
            waveIndex = 0;
        }
        //Debug.Log(waveIndex);
    }

    void Update()
    {
        activeWavesString = activeWaves.text;
        //Debug.Log("wave index: " + waveIndex + "\nwaves.Length: " + waves.Length);
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
        //Debug.Log(PlayerStats.Rounds + " wave spawned");
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
        if(index >= 0)
            activeWaves.text = activeWaves.text.Remove(index, (waveString + " ").Length);
        
    
        
        //activeCoroutines.Remove(coroutine);
    }

    void SpawnEnemy(GameObject enemy)
    {   
        float randomNumber = Random.Range(-1f, 1f);
        Vector3 spawnStagger = spawnPoint.position + new Vector3(randomNumber, 0, randomNumber);
        Instantiate(enemy, spawnPoint.position, spawnPoint.rotation);
    }
    
    public void SendNextWave()
    {
        //Debug.Log("Enemies alive: " + EnemiesAlive);
        if(waveIndex != waves.Length || waveIndex < 1)
        {
            Wave wave = waves[waveIndex];
            Coroutine coroutine = StartCoroutine(SpawnWave(wave));
            if(gameManager.reverseMode){
                waveIndex--;
            }
            else{
                waveIndex++;
            }
            
            activeCoroutines.Add(coroutine);
            countdown = wave.nextWaveDelay;
        }
        else
        {
            //Debug.Log("Max wave reached!");
        }
    }

    private bool AnyEnemiesAlive()
{
    // Scan by tag; only consider active enemies that aren’t dead yet
    var gos = GameObject.FindGameObjectsWithTag("Enemy");
    for (int i = 0; i < gos.Length; i++)
    {
        var go = gos[i];
        if (!go || !go.activeInHierarchy) continue;

        if (go.TryGetComponent<Enemy>(out var e))
        {
            if (!e.isDead) return true;   // still alive
        }
        else
        {
            // Tagged "enemy" but no Enemy component? Treat as alive to be safe.
            return true;
        }
    }
    return false;
}

}
